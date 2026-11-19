using SareeBilling.Domain.Enums;

namespace SareeBilling.Application.Invoices;

public sealed record InvoiceSearchCriteria
{
    /// <summary>
    /// Digits only (e.g. "161") → that exact invoice number.
    /// Anything else → part of the customer name or GSTIN (or an older invoice number).
    /// </summary>
    public string? Text { get; init; }

    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public bool IncludeCancelled { get; init; } = true;
}

public sealed record InvoiceSummary(
    int Id,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    string BuyerName,
    string? BuyerGstin,
    decimal TaxableTotal,
    decimal TotalTax,
    decimal GrandTotal,
    InvoiceStatus Status)
{
    /// <summary>Total of the credit notes (not cancelled) issued against this invoice.</summary>
    public decimal Credited { get; init; }
}
