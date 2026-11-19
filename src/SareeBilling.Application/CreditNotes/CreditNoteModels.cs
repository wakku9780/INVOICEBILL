using SareeBilling.Domain.Enums;

namespace SareeBilling.Application.CreditNotes;

/// <summary>One invoice line with how much of it has already been credited by earlier (not cancelled) credit notes.</summary>
public sealed record CreditableLine(
    int InvoiceItemId,
    int LineNumber,
    string Description,
    string HsnCode,
    decimal Quantity,
    string Unit,
    decimal Rate,
    decimal TaxableAmount,
    decimal GstRate,
    decimal ReturnedQuantity,
    decimal CreditedTaxable,
    decimal AmountOnlyCredited)
{
    /// <summary>Quantity that can still be returned.</summary>
    public decimal RemainingQuantity => Quantity - ReturnedQuantity;

    /// <summary>Taxable value that can still be credited.</summary>
    public decimal RemainingTaxable => TaxableAmount - CreditedTaxable;
}

/// <summary>A credit note already issued against an invoice.</summary>
public sealed record IssuedCreditNote(int Id, string CreditNoteNumber, DateOnly CreditNoteDate, decimal GrandTotal);

/// <summary>A saved invoice as seen when making a credit note against it.</summary>
public sealed record CreditableInvoice(
    int InvoiceId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    int CustomerId,
    string BuyerName,
    string BuyerAddress,
    string? BuyerGstin,
    string BuyerState,
    string BuyerStateCode,
    string? BuyerPhone,
    TaxType TaxType,
    decimal GrandTotal,
    bool IsCancelled,
    IReadOnlyList<CreditableLine> Lines,
    IReadOnlyList<IssuedCreditNote> CreditNotes)
{
    public decimal AlreadyCredited => CreditNotes.Sum(c => c.GrandTotal);
}

/// <summary>For goods returned: <see cref="Quantity"/>. For an amount-only credit note: <see cref="TaxableAmount"/>.</summary>
public sealed record CreditNoteLineRequest(int InvoiceItemId, decimal Quantity, decimal TaxableAmount);

public sealed record CreateCreditNoteRequest
{
    public int InvoiceId { get; init; }
    public DateOnly CreditNoteDate { get; init; }
    public CreditNoteKind Kind { get; init; } = CreditNoteKind.GoodsReturn;
    public string? Reason { get; init; }
    public IReadOnlyList<CreditNoteLineRequest> Lines { get; init; } = [];
}

/// <summary>A worked-out credit note line (what will be saved and printed).</summary>
public sealed record CreditNoteLineResult(
    CreditableLine Source,
    decimal Quantity,
    decimal Rate,
    decimal TaxableAmount,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal CgstRate,
    decimal SgstRate,
    decimal IgstRate)
{
    public decimal TotalTax => CgstAmount + SgstAmount + IgstAmount;
    public decimal LineTotal => TaxableAmount + TotalTax;
}

/// <summary>Totals of a credit note, worked out exactly as an invoice is (tax per line, total rounded to the rupee).</summary>
public sealed record CreditNoteCalculation(
    IReadOnlyList<CreditNoteLineResult> Lines,
    decimal TotalQuantity,
    decimal TaxableTotal,
    decimal CgstTotal,
    decimal SgstTotal,
    decimal IgstTotal,
    decimal TotalTax,
    decimal RoundOff,
    decimal GrandTotal);

/// <param name="AppliedToInvoice">Part of the credit note that reduced the invoice's pending amount.</param>
/// <param name="KeptAsAdvance">The rest, held as the customer's advance.</param>
public sealed record CreatedCreditNote(int Id, string CreditNoteNumber, decimal GrandTotal, decimal AppliedToInvoice, decimal KeptAsAdvance);

public sealed record CreditNoteSearchCriteria
{
    public string? Text { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public bool IncludeCancelled { get; init; } = true;
}

public sealed record CreditNoteSummary(
    int Id,
    string CreditNoteNumber,
    DateOnly CreditNoteDate,
    CreditNoteKind Kind,
    int InvoiceId,
    string InvoiceNumber,
    int CustomerId,
    string BuyerName,
    decimal TaxableTotal,
    decimal TotalTax,
    decimal GrandTotal,
    string Reason,
    VoucherStatus Status);

/// <summary>A credit note reduced to what the dashboard needs.</summary>
public sealed record CreditNoteRow(int Id, DateOnly CreditNoteDate, int CustomerId, decimal TaxableTotal, decimal TotalTax, decimal GrandTotal);

public static class CreditNoteMessages
{
    /// <example>"Invoice 161 has credit note CN-3. Cancel the credit note first, then the invoice."</example>
    public static string BlockInvoiceCancel(string invoiceNumber, IReadOnlyList<string> creditNotes) =>
        creditNotes.Count == 1
            ? $"Invoice {invoiceNumber} has credit note {creditNotes[0]}. Cancel the credit note first, then the invoice."
            : $"Invoice {invoiceNumber} has credit notes {string.Join(", ", creditNotes)}. Cancel those credit notes first, then the invoice.";
}

public static class CreditNoteKinds
{
    public static string Label(CreditNoteKind kind) => kind switch
    {
        CreditNoteKind.GoodsReturn => "Goods returned",
        CreditNoteKind.AmountOnly => "Rate difference / discount",
        _ => kind.ToString()
    };
}
