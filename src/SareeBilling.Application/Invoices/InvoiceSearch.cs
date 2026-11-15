using SareeBilling.Domain.Enums;

namespace SareeBilling.Application.Invoices;

public sealed record InvoiceSearchCriteria
{
    /// <summary>Matches part of the invoice number or the customer name.</summary>
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
    InvoiceStatus Status);
