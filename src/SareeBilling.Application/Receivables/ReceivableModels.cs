using SareeBilling.Domain.Enums;

namespace SareeBilling.Application.Receivables;

/// <summary>Something a customer has to pay: a sales invoice or an old (opening) bill.</summary>
public enum BillKind
{
    Invoice = 1,
    OpeningBill = 2
}

public readonly record struct BillRef(BillKind Kind, int Id);

/// <summary>Value on the customer's side: a receipt, an old (opening) advance or a credit note.</summary>
public enum CreditKind
{
    Receipt = 1,
    OpeningCredit = 2,
    CreditNote = 3
}

public readonly record struct CreditRef(CreditKind Kind, int Id);

/// <summary>A bill with how much of it has been paid. Cancelled invoices are never included.</summary>
public sealed record AccountBill(
    BillRef Ref,
    string Number,
    DateOnly Date,
    DateOnly DueDate,
    decimal Total,
    decimal Settled,
    string Particulars)
{
    public decimal Pending => Total - Settled;

    public bool IsOpen => Pending > 0;

    /// <summary>0 when not yet due.</summary>
    public int DaysOverdue(DateOnly today) => Math.Max(0, today.DayNumber - DueDate.DayNumber);
}

/// <summary>A receipt, opening advance or credit note with how much of it has been matched to bills. Cancelled ones are never included.</summary>
public sealed record AccountCredit(
    CreditRef Ref,
    string Number,
    DateOnly Date,
    decimal Amount,
    decimal Used,
    string Particulars,
    IReadOnlyList<string> SettledBillNumbers)
{
    /// <summary>The part not matched to any bill: the customer's advance.</summary>
    public decimal Unused => Amount - Used;
}

/// <summary>Everything a customer owes and has paid, bill by bill.</summary>
public sealed record CustomerAccount(
    int CustomerId,
    string CustomerName,
    string City,
    int CreditDays,
    IReadOnlyList<AccountBill> Bills,
    IReadOnlyList<AccountCredit> Credits)
{
    /// <summary>Unpaid amount of all bills.</summary>
    public decimal Pending => Bills.Sum(b => b.Pending);

    /// <summary>Money received but not yet matched to a bill.</summary>
    public decimal Advance => Credits.Sum(c => c.Unused);

    /// <summary>Positive = the customer owes you (Dr); negative = you hold an advance (Cr).</summary>
    public decimal Balance => Bills.Sum(b => b.Total) - Credits.Sum(c => c.Amount);

    /// <summary>Unpaid bills, oldest first: the order in which money is matched automatically.</summary>
    public IReadOnlyList<AccountBill> OpenBills => Bills
        .Where(b => b.IsOpen)
        .OrderBy(b => b.Date)
        .ThenBy(b => b.Ref.Kind == BillKind.OpeningBill ? 0 : 1)
        .ThenBy(b => b.Ref.Id)
        .ToList();

    /// <summary>Receipts and advances with money left over, oldest first.</summary>
    public IReadOnlyList<AccountCredit> UnusedCredits => Credits
        .Where(c => c.Unused > 0)
        .OrderBy(c => c.Date)
        .ThenBy(c => c.Ref.Kind == CreditKind.OpeningCredit ? 0 : 1)
        .ThenBy(c => c.Ref.Id)
        .ToList();
}

/// <summary>"Pay this much of this bill."</summary>
public sealed record AllocationLine(BillRef Bill, decimal Amount);

/// <summary>"Use this much of this credit to pay this bill."</summary>
public sealed record PlannedAllocation(CreditRef Credit, BillRef Bill, decimal Amount);

public sealed record CreateReceiptRequest
{
    public int CustomerId { get; init; }
    public DateOnly ReceiptDate { get; init; }
    public decimal Amount { get; init; }
    public PaymentMode Mode { get; init; } = PaymentMode.BankTransfer;
    public string? Reference { get; init; }
    public string? Narration { get; init; }

    /// <summary>Bills paid by this receipt. Whatever is not matched stays as advance.</summary>
    public IReadOnlyList<AllocationLine> Allocations { get; init; } = [];
}

public sealed record CreatedReceipt(int Id, string ReceiptNumber, decimal Amount, decimal KeptAsAdvance);

public sealed record ReceiptSearchCriteria
{
    public string? Text { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public bool IncludeCancelled { get; init; } = true;
}

public sealed record ReceiptSummary(
    int Id,
    string ReceiptNumber,
    DateOnly ReceiptDate,
    int CustomerId,
    string CustomerName,
    decimal Amount,
    PaymentMode Mode,
    string? Reference,
    VoucherStatus Status);

public sealed record ReceiptAllocationDetail(string BillNumber, DateOnly BillDate, decimal Amount);

public sealed record ReceiptDetail(
    int Id,
    string ReceiptNumber,
    DateOnly ReceiptDate,
    int CustomerId,
    string CustomerName,
    string CustomerAddress,
    string? CustomerGstin,
    decimal Amount,
    PaymentMode Mode,
    string? Reference,
    string? Narration,
    VoucherStatus Status,
    string? CancellationReason,
    IReadOnlyList<ReceiptAllocationDetail> Allocations)
{
    public decimal KeptAsAdvance => Amount - Allocations.Sum(a => a.Amount);
}

public sealed record OpeningBillDto
{
    public int Id { get; init; }
    public int CustomerId { get; init; }
    public string BillNumber { get; init; } = string.Empty;
    public DateOnly BillDate { get; init; }
    public DateOnly DueDate { get; init; }
    public BalanceSide Side { get; init; } = BalanceSide.Debit;
    public decimal Amount { get; init; }

    /// <summary>Amount already matched (paid, or for an advance: used). An entry in use cannot be removed.</summary>
    public decimal Used { get; init; }
}

/// <summary>A receipt reduced to what the dashboard needs.</summary>
public sealed record ReceiptRow(int Id, DateOnly ReceiptDate, int CustomerId, decimal Amount);

public static class PaymentModes
{
    public static string Label(PaymentMode mode) => mode switch
    {
        PaymentMode.Cash => "Cash",
        PaymentMode.BankTransfer => "Bank / UPI",
        PaymentMode.Cheque => "Cheque",
        _ => mode.ToString()
    };
}
