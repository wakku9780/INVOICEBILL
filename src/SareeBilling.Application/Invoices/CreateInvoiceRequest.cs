namespace SareeBilling.Application.Invoices;

/// <summary>What the user entered for one line. Amounts are calculated by the service, never taken from the UI.</summary>
public sealed record InvoiceItemInput
{
    public string Description { get; init; } = string.Empty;
    public string HsnCode { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public string Unit { get; init; } = string.Empty;
    public decimal Rate { get; init; }
    public decimal GstRate { get; init; }
}

public sealed record CreateInvoiceRequest
{
    public int CustomerId { get; init; }
    public DateOnly InvoiceDate { get; init; }

    public string? BuyerOrderNumber { get; init; }
    public DateOnly? BuyerOrderDate { get; init; }
    public string? PaymentTerms { get; init; }
    public string? DispatchDocumentNumber { get; init; }
    public string? DispatchedThrough { get; init; }
    public string? Destination { get; init; }
    public string? TermsOfDelivery { get; init; }

    public IReadOnlyList<InvoiceItemInput> Items { get; init; } = [];
}

public sealed record CreatedInvoice(int Id, string InvoiceNumber, decimal GrandTotal);
