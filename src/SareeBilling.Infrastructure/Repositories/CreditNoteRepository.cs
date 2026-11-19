using Microsoft.EntityFrameworkCore;
using SareeBilling.Application.Abstractions;
using SareeBilling.Application.CreditNotes;
using SareeBilling.Application.Receivables;
using SareeBilling.Domain.Entities;
using SareeBilling.Domain.Enums;
using SareeBilling.Infrastructure.Persistence;

namespace SareeBilling.Infrastructure.Repositories;

internal sealed class CreditNoteRepository(IDbContextFactory<AppDbContext> contextFactory) : ICreditNoteRepository
{
    private const string Prefix = "CN-";

    public async Task<CreditableInvoice?> GetCreditableInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await LoadAsync(db, invoiceId, cancellationToken);
    }

    public async Task<int?> FindInvoiceIdAsync(string invoiceNumber, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var number = invoiceNumber.Trim();
        return await db.Invoices.AsNoTracking()
            .Where(i => i.InvoiceNumber == number)
            .Select(i => (int?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(CreditNote CreditNote, decimal AppliedToInvoice)> AddAsync(
        int invoiceId, Func<CreditableInvoice, CreditNote> build, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // BEGIN IMMEDIATE: nothing else can write between reading what is already credited and saving.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var invoice = await LoadAsync(db, invoiceId, cancellationToken)
                      ?? throw new Application.Common.ValidationException(["This invoice no longer exists."]);
        var note = build(invoice);

        var highest = await db.CreditNotes.MaxAsync(c => (int?)c.SequenceNumber, cancellationToken) ?? 0;
        var next = await ReceivablesRepository.NextVoucherNumberAsync(db, VoucherType.CreditNote, highest, cancellationToken);
        note.SequenceNumber = next;
        note.CreditNoteNumber = Prefix + next;
        db.CreditNotes.Add(note);
        await db.SaveChangesAsync(cancellationToken);

        // Reduce what is still pending on the invoice; whatever is left stays as advance.
        var account = (await ReceivablesRepository.LoadAccountsAsync(db, note.CustomerId, cancellationToken)).Single();
        var bill = account.Bills.SingleOrDefault(b => b.Ref == new BillRef(BillKind.Invoice, invoiceId));
        var applied = Math.Min(bill?.Pending ?? 0m, note.GrandTotal);
        if (applied > 0)
        {
            db.BillAllocations.Add(ReceivablesRepository.NewAllocation(
                note.CustomerId, note.CreditNoteDate, new CreditRef(CreditKind.CreditNote, note.Id), bill!.Ref, applied));
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (note, applied);
    }

    public async Task CancelAsync(int creditNoteId, string reason, DateTime cancelledAtUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var note = await db.CreditNotes.FindAsync([creditNoteId], cancellationToken)
                   ?? throw new InvalidOperationException("This credit note no longer exists.");

        // The invoice (and any bill its advance was used for) becomes pending again.
        await db.BillAllocations.Where(a => a.CreditNoteId == creditNoteId).ExecuteDeleteAsync(cancellationToken);

        note.Status = VoucherStatus.Cancelled;
        note.CancellationReason = reason;
        note.CancelledAtUtc = cancelledAtUtc;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CreditNote?> GetAsync(int creditNoteId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.CreditNotes.AsNoTracking()
            .Include(c => c.Items.OrderBy(i => i.LineNumber))
            .SingleOrDefaultAsync(c => c.Id == creditNoteId, cancellationToken);
    }

    public async Task<IReadOnlyList<CreditNoteSummary>> SearchAsync(CreditNoteSearchCriteria criteria, int maxResults, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<CreditNote> query = db.CreditNotes.AsNoTracking();

        var text = criteria.Text?.Trim();
        if (!string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit))
        {
            // "3" finds credit note CN-3 and every credit note against invoice 3.
            var number = Prefix + text;
            query = query.Where(c => c.CreditNoteNumber == number || c.InvoiceNumber == text);
        }
        else if (!string.IsNullOrEmpty(text))
        {
            var pattern = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(c =>
                EF.Functions.Like(c.CreditNoteNumber, pattern, "\\") ||
                EF.Functions.Like(c.InvoiceNumber, pattern, "\\") ||
                EF.Functions.Like(c.BuyerName, pattern, "\\") ||
                EF.Functions.Like(c.Reason, pattern, "\\"));
        }

        if (criteria.FromDate is { } from)
        {
            query = query.Where(c => c.CreditNoteDate >= from);
        }

        if (criteria.ToDate is { } to)
        {
            query = query.Where(c => c.CreditNoteDate <= to);
        }

        if (!criteria.IncludeCancelled)
        {
            query = query.Where(c => c.Status != VoucherStatus.Cancelled);
        }

        return await query
            .OrderByDescending(c => c.CreditNoteDate)
            .ThenByDescending(c => c.SequenceNumber)
            .Take(maxResults)
            .Select(c => new CreditNoteSummary(
                c.Id, c.CreditNoteNumber, c.CreditNoteDate, c.Kind, c.InvoiceId, c.InvoiceNumber, c.CustomerId, c.BuyerName,
                c.TaxableTotal, c.TotalTax, c.GrandTotal, c.Reason, c.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CreditNoteRow>> ListAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.CreditNotes.AsNoTracking()
            .Where(c => c.Status == VoucherStatus.Posted && c.CreditNoteDate >= from && c.CreditNoteDate <= to)
            .Select(c => new CreditNoteRow(c.Id, c.CreditNoteDate, c.CustomerId, c.TaxableTotal, c.TotalTax, c.GrandTotal))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetActiveNumbersForInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ActiveNumbersAsync(db, invoiceId, cancellationToken);
    }

    internal static async Task<List<string>> ActiveNumbersAsync(AppDbContext db, int invoiceId, CancellationToken cancellationToken) =>
        await db.CreditNotes.AsNoTracking()
            .Where(c => c.InvoiceId == invoiceId && c.Status == VoucherStatus.Posted)
            .OrderBy(c => c.SequenceNumber)
            .Select(c => c.CreditNoteNumber)
            .ToListAsync(cancellationToken);

    private static async Task<CreditableInvoice?> LoadAsync(AppDbContext db, int invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.AsNoTracking()
            .Include(i => i.Items)
            .SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var notes = await db.CreditNotes.AsNoTracking()
            .Where(c => c.InvoiceId == invoiceId && c.Status == VoucherStatus.Posted)
            .Include(c => c.Items)
            .ToListAsync(cancellationToken);

        var items = notes.SelectMany(n => n.Items.Select(i => (n.Kind, Item: i))).ToList();
        var lines = invoice.Items
            .OrderBy(i => i.LineNumber)
            .Select(i =>
            {
                var credited = items.Where(x => x.Item.InvoiceItemId == i.Id).ToList();
                return new CreditableLine(
                    i.Id, i.LineNumber, i.Description, i.HsnCode, i.Quantity, i.Unit, i.Rate, i.TaxableAmount, i.GstRate,
                    ReturnedQuantity: credited.Sum(x => x.Item.Quantity),
                    CreditedTaxable: credited.Sum(x => x.Item.TaxableAmount),
                    AmountOnlyCredited: credited.Where(x => x.Kind == CreditNoteKind.AmountOnly).Sum(x => x.Item.TaxableAmount));
            })
            .ToList();

        return new CreditableInvoice(
            invoice.Id, invoice.InvoiceNumber, invoice.InvoiceDate, invoice.CustomerId,
            invoice.BuyerName, invoice.BuyerAddress, invoice.BuyerGstin, invoice.BuyerState, invoice.BuyerStateCode, invoice.BuyerPhone,
            invoice.TaxType, invoice.GrandTotal, invoice.Status == InvoiceStatus.Cancelled,
            lines,
            notes.OrderBy(n => n.SequenceNumber)
                .Select(n => new IssuedCreditNote(n.Id, n.CreditNoteNumber, n.CreditNoteDate, n.GrandTotal))
                .ToList());
    }
}
