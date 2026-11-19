using Microsoft.EntityFrameworkCore;
using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Receivables;
using SareeBilling.Domain.Common;
using SareeBilling.Domain.Entities;
using SareeBilling.Domain.Enums;
using SareeBilling.Infrastructure.Persistence;

namespace SareeBilling.Infrastructure.Repositories;

internal sealed class ReceivablesRepository(IDbContextFactory<AppDbContext> contextFactory) : IReceivablesRepository
{
    private const string ReceiptPrefix = "R-";

    public async Task<CustomerAccount?> GetAccountAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return (await LoadAccountsAsync(db, customerId, cancellationToken)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<CustomerAccount>> GetAllAccountsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await LoadAccountsAsync(db, null, cancellationToken);
    }

    public async Task<Receipt> AddReceiptAsync(
        Receipt receipt,
        Func<CustomerAccount, IReadOnlyList<AllocationLine>> checkAndPlan,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // BEGIN IMMEDIATE: nothing else can write between reading the pending amounts and saving.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var account = (await LoadAccountsAsync(db, receipt.CustomerId, cancellationToken)).Single();
        var lines = checkAndPlan(account);

        var next = await NextNumberAsync(db, cancellationToken);
        receipt.SequenceNumber = next;
        receipt.ReceiptNumber = ReceiptPrefix + next;
        db.Receipts.Add(receipt);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var line in lines)
        {
            db.BillAllocations.Add(NewAllocation(receipt.CustomerId, receipt.ReceiptDate, new CreditRef(CreditKind.Receipt, receipt.Id), line.Bill, line.Amount));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return receipt;
    }

    public async Task AddAllocationsAsync(
        int customerId,
        DateOnly allocatedOn,
        Func<CustomerAccount, IReadOnlyList<PlannedAllocation>> checkAndPlan,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var account = (await LoadAccountsAsync(db, customerId, cancellationToken)).SingleOrDefault()
                      ?? throw new InvalidOperationException("This customer no longer exists.");

        foreach (var plan in checkAndPlan(account))
        {
            db.BillAllocations.Add(NewAllocation(customerId, allocatedOn, plan.Credit, plan.Bill, plan.Amount));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelReceiptAsync(int receiptId, string reason, DateTime cancelledAtUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var receipt = await db.Receipts.FindAsync([receiptId], cancellationToken)
                      ?? throw new InvalidOperationException("This receipt no longer exists.");

        // Its bills become pending again.
        await db.BillAllocations.Where(a => a.ReceiptId == receiptId).ExecuteDeleteAsync(cancellationToken);

        receipt.Status = VoucherStatus.Cancelled;
        receipt.CancellationReason = reason;
        receipt.CancelledAtUtc = cancelledAtUtc;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ReceiptDetail?> GetReceiptAsync(int receiptId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var r = await db.Receipts.AsNoTracking().Include(x => x.Customer).SingleOrDefaultAsync(x => x.Id == receiptId, cancellationToken);
        if (r is null)
        {
            return null;
        }

        var allocations = await db.BillAllocations.AsNoTracking()
            .Where(a => a.ReceiptId == receiptId)
            .Select(a => new
            {
                a.Amount,
                InvoiceNumber = a.Invoice != null ? a.Invoice.InvoiceNumber : null,
                InvoiceDate = a.Invoice != null ? (DateOnly?)a.Invoice.InvoiceDate : null,
                OpeningNumber = a.OpeningBill != null ? a.OpeningBill.BillNumber : null,
                OpeningDate = a.OpeningBill != null ? (DateOnly?)a.OpeningBill.BillDate : null
            })
            .ToListAsync(cancellationToken);

        var c = r.Customer;
        return new ReceiptDetail(
            r.Id, r.ReceiptNumber, r.ReceiptDate, r.CustomerId, r.CustomerName,
            PostalAddress.Format(c.AddressLine1, c.AddressLine2, c.City, c.Pincode), c.Gstin,
            r.Amount, r.Mode, r.Reference, r.Narration, r.Status, r.CancellationReason,
            allocations
                .Select(a => a.InvoiceNumber is not null
                    ? new ReceiptAllocationDetail(a.InvoiceNumber, a.InvoiceDate!.Value, a.Amount)
                    : new ReceiptAllocationDetail(OldBillLabel(a.OpeningNumber!), a.OpeningDate!.Value, a.Amount))
                .OrderBy(a => a.BillDate)
                .ToList());
    }

    public async Task<IReadOnlyList<ReceiptSummary>> SearchReceiptsAsync(ReceiptSearchCriteria criteria, int maxResults, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<Receipt> query = db.Receipts.AsNoTracking();

        var text = criteria.Text?.Trim();
        if (!string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit))
        {
            // "12" finds receipt R-12 exactly.
            var number = ReceiptPrefix + text;
            query = query.Where(r => r.ReceiptNumber == number);
        }
        else if (!string.IsNullOrEmpty(text))
        {
            var pattern = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(r =>
                EF.Functions.Like(r.ReceiptNumber, pattern, "\\") ||
                EF.Functions.Like(r.CustomerName, pattern, "\\") ||
                EF.Functions.Like(r.Reference, pattern, "\\"));
        }

        if (criteria.FromDate is { } from)
        {
            query = query.Where(r => r.ReceiptDate >= from);
        }

        if (criteria.ToDate is { } to)
        {
            query = query.Where(r => r.ReceiptDate <= to);
        }

        if (!criteria.IncludeCancelled)
        {
            query = query.Where(r => r.Status != VoucherStatus.Cancelled);
        }

        return await query
            .OrderByDescending(r => r.ReceiptDate)
            .ThenByDescending(r => r.SequenceNumber)
            .Take(maxResults)
            .Select(r => new ReceiptSummary(r.Id, r.ReceiptNumber, r.ReceiptDate, r.CustomerId, r.CustomerName, r.Amount, r.Mode, r.Reference, r.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReceiptRow>> ListReceiptsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Receipts.AsNoTracking()
            .Where(r => r.Status == VoucherStatus.Posted && r.ReceiptDate >= from && r.ReceiptDate <= to)
            .Select(r => new ReceiptRow(r.Id, r.ReceiptDate, r.CustomerId, r.Amount))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OpeningBillDto>> GetOpeningBillsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var bills = await db.OpeningBills.AsNoTracking().Where(b => b.CustomerId == customerId).ToListAsync(cancellationToken);
        var allocations = await db.BillAllocations.AsNoTracking()
            .Where(a => a.CustomerId == customerId && (a.OpeningBillId != null || a.OpeningCreditId != null))
            .Select(a => new { a.OpeningBillId, a.OpeningCreditId, a.Amount })
            .ToListAsync(cancellationToken);

        return bills
            .OrderBy(b => b.BillDate).ThenBy(b => b.Id)
            .Select(b => new OpeningBillDto
            {
                Id = b.Id,
                CustomerId = b.CustomerId,
                BillNumber = b.BillNumber,
                BillDate = b.BillDate,
                DueDate = b.DueDate,
                Side = b.Side,
                Amount = b.Amount,
                Used = allocations.Where(a => a.OpeningBillId == b.Id || a.OpeningCreditId == b.Id).Sum(a => a.Amount)
            })
            .ToList();
    }

    public async Task<int> AddOpeningBillAsync(OpeningBill bill, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.OpeningBills.Add(bill);
        await db.SaveChangesAsync(cancellationToken);
        return bill.Id;
    }

    public async Task<bool> RemoveOpeningBillAsync(int openingBillId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (await db.BillAllocations.AnyAsync(a => a.OpeningBillId == openingBillId || a.OpeningCreditId == openingBillId, cancellationToken))
        {
            return false;
        }

        await db.OpeningBills.Where(b => b.Id == openingBillId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<decimal> GetSettledAmountAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var amounts = await db.BillAllocations.AsNoTracking()
            .Where(a => a.InvoiceId == invoiceId)
            .Select(a => a.Amount)
            .ToListAsync(cancellationToken);
        return amounts.Sum();
    }

    /// <summary>"Old bill 145" — how an opening bill is shown next to invoice numbers.</summary>
    internal static string OldBillLabel(string billNumber) => "Old bill " + billNumber;

    internal static BillAllocation NewAllocation(int customerId, DateOnly on, CreditRef credit, BillRef bill, decimal amount) => new()
    {
        CustomerId = customerId,
        AllocatedOn = on,
        ReceiptId = credit.Kind == CreditKind.Receipt ? credit.Id : null,
        OpeningCreditId = credit.Kind == CreditKind.OpeningCredit ? credit.Id : null,
        CreditNoteId = credit.Kind == CreditKind.CreditNote ? credit.Id : null,
        InvoiceId = bill.Kind == BillKind.Invoice ? bill.Id : null,
        OpeningBillId = bill.Kind == BillKind.OpeningBill ? bill.Id : null,
        Amount = amount
    };

    /// <summary>The larger of the counter and the highest saved receipt, plus one; the counter is moved on.</summary>
    private static async Task<int> NextNumberAsync(AppDbContext db, CancellationToken cancellationToken) =>
        await NextVoucherNumberAsync(db, VoucherType.Receipt,
            await db.Receipts.MaxAsync(r => (int?)r.SequenceNumber, cancellationToken) ?? 0, cancellationToken);

    /// <summary>The larger of the series counter and <paramref name="highestUsed"/>, plus one; the counter is moved on.</summary>
    internal static async Task<int> NextVoucherNumberAsync(AppDbContext db, VoucherType type, int highestUsed, CancellationToken cancellationToken)
    {
        var counter = await db.VoucherCounters.FindAsync([type], cancellationToken);
        var next = Math.Max(counter?.LastNumber ?? 0, highestUsed) + 1;

        if (counter is null)
        {
            db.VoucherCounters.Add(new VoucherCounter { VoucherType = type, LastNumber = next });
        }
        else
        {
            counter.LastNumber = next;
        }

        return next;
    }

    /// <summary>Builds bill-wise accounts from saved documents. Amounts are TEXT in SQLite, so they are added up here.</summary>
    internal static async Task<List<CustomerAccount>> LoadAccountsAsync(AppDbContext db, int? customerId, CancellationToken cancellationToken)
    {
        var customers = await db.Customers.AsNoTracking()
            .Where(c => customerId == null || c.Id == customerId)
            .Select(c => new { c.Id, c.Name, c.City, c.CreditDays })
            .ToListAsync(cancellationToken);

        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Final && (customerId == null || i.CustomerId == customerId))
            .Select(i => new { i.Id, i.CustomerId, i.InvoiceNumber, i.InvoiceDate, i.DueDate, i.GrandTotal, i.TaxableTotal, i.TotalTax })
            .ToListAsync(cancellationToken);

        var openings = await db.OpeningBills.AsNoTracking()
            .Where(b => customerId == null || b.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        var receipts = await db.Receipts.AsNoTracking()
            .Where(r => r.Status == VoucherStatus.Posted && (customerId == null || r.CustomerId == customerId))
            .Select(r => new { r.Id, r.CustomerId, r.ReceiptNumber, r.ReceiptDate, r.Amount, r.Mode, r.Reference })
            .ToListAsync(cancellationToken);

        var creditNotes = await db.CreditNotes.AsNoTracking()
            .Where(c => c.Status == VoucherStatus.Posted && (customerId == null || c.CustomerId == customerId))
            .Select(c => new { c.Id, c.CustomerId, c.CreditNoteNumber, c.CreditNoteDate, c.Kind, c.InvoiceNumber, c.GrandTotal, c.Reason })
            .ToListAsync(cancellationToken);

        var allocations = await db.BillAllocations.AsNoTracking()
            .Where(a => customerId == null || a.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        var billNumbers = invoices.ToDictionary(i => new BillRef(BillKind.Invoice, i.Id), i => i.InvoiceNumber);
        foreach (var o in openings.Where(o => o.Side == BalanceSide.Debit))
        {
            billNumbers[new BillRef(BillKind.OpeningBill, o.Id)] = OldBillLabel(o.BillNumber);
        }

        static BillRef TargetOf(BillAllocation a) =>
            a.InvoiceId is { } i ? new BillRef(BillKind.Invoice, i) : new BillRef(BillKind.OpeningBill, a.OpeningBillId!.Value);

        static CreditRef SourceOf(BillAllocation a) =>
            a.ReceiptId is { } r ? new CreditRef(CreditKind.Receipt, r)
            : a.CreditNoteId is { } c ? new CreditRef(CreditKind.CreditNote, c)
            : new CreditRef(CreditKind.OpeningCredit, a.OpeningCreditId!.Value);

        var settledByBill = allocations.GroupBy(TargetOf).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
        var allocationsByCredit = allocations.GroupBy(SourceOf).ToDictionary(g => g.Key, g => g.ToList());

        var accounts = new List<CustomerAccount>();
        foreach (var customer in customers)
        {
            var bills = new List<AccountBill>();
            var credits = new List<AccountCredit>();

            foreach (var i in invoices.Where(i => i.CustomerId == customer.Id))
            {
                var key = new BillRef(BillKind.Invoice, i.Id);
                bills.Add(new AccountBill(key, i.InvoiceNumber, i.InvoiceDate, i.DueDate, i.GrandTotal,
                    settledByBill.GetValueOrDefault(key),
                    $"Taxable {IndianFormat.Amount(i.TaxableTotal)} + GST {IndianFormat.Amount(i.TotalTax)}"));
            }

            foreach (var o in openings.Where(o => o.CustomerId == customer.Id))
            {
                if (o.Side == BalanceSide.Debit)
                {
                    var key = new BillRef(BillKind.OpeningBill, o.Id);
                    bills.Add(new AccountBill(key, OldBillLabel(o.BillNumber), o.BillDate, o.DueDate, o.Amount,
                        settledByBill.GetValueOrDefault(key), "Unpaid bill from before using this app"));
                }
                else
                {
                    var key = new CreditRef(CreditKind.OpeningCredit, o.Id);
                    var used = allocationsByCredit.GetValueOrDefault(key) ?? [];
                    credits.Add(new AccountCredit(key, o.BillNumber, o.BillDate, o.Amount, used.Sum(a => a.Amount),
                        "Advance from before using this app", NumbersOf(used)));
                }
            }

            foreach (var r in receipts.Where(r => r.CustomerId == customer.Id))
            {
                var key = new CreditRef(CreditKind.Receipt, r.Id);
                var used = allocationsByCredit.GetValueOrDefault(key) ?? [];
                var usedTotal = used.Sum(a => a.Amount);
                var parts = new List<string> { PaymentModes.Label(r.Mode) };
                if (!string.IsNullOrWhiteSpace(r.Reference))
                {
                    parts.Add(r.Reference!);
                }

                var settled = NumbersOf(used);
                if (settled.Count > 0)
                {
                    parts.Add("against " + string.Join(", ", settled));
                }

                if (usedTotal < r.Amount)
                {
                    parts.Add("advance " + IndianFormat.Amount(r.Amount - usedTotal));
                }

                credits.Add(new AccountCredit(key, r.ReceiptNumber, r.ReceiptDate, r.Amount, usedTotal, string.Join(" · ", parts), settled));
            }

            foreach (var c in creditNotes.Where(c => c.CustomerId == customer.Id))
            {
                var key = new CreditRef(CreditKind.CreditNote, c.Id);
                var used = allocationsByCredit.GetValueOrDefault(key) ?? [];
                var usedTotal = used.Sum(a => a.Amount);
                var parts = new List<string>
                {
                    (c.Kind == CreditNoteKind.GoodsReturn ? "Goods returned" : "Rate difference / discount") + " on invoice " + c.InvoiceNumber,
                    c.Reason
                };

                if (usedTotal < c.GrandTotal)
                {
                    parts.Add("advance " + IndianFormat.Amount(c.GrandTotal - usedTotal));
                }

                credits.Add(new AccountCredit(key, c.CreditNoteNumber, c.CreditNoteDate, c.GrandTotal, usedTotal,
                    string.Join(" · ", parts), NumbersOf(used)));
            }

            if (customerId is null && bills.Count == 0 && credits.Count == 0)
            {
                continue;
            }

            accounts.Add(new CustomerAccount(customer.Id, customer.Name, customer.City, customer.CreditDays, bills, credits));
        }

        return accounts;

        List<string> NumbersOf(List<BillAllocation> used) => used
            .Select(TargetOf)
            .Distinct()
            .Select(b => billNumbers.GetValueOrDefault(b, "?"))
            .ToList();
    }
}
