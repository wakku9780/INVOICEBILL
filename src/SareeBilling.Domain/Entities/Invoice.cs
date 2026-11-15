using SareeBilling.Domain.Common;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Domain.Entities;

/// <summary>
/// A GST tax invoice. Seller and buyer details are snapshotted at save time and
/// every calculated amount is persisted, so a reprint always matches the original.
/// </summary>
public class Invoice : AuditableEntity
{
    /// <summary>Full display number, e.g. "JAS/2026-27/161". Unique.</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>Financial year label, e.g. "2026-27".</summary>
    public string FinancialYear { get; set; } = string.Empty;

    /// <summary>Running number within the financial year. Unique per year.</summary>
    public int SequenceNumber { get; set; }

    public DateOnly InvoiceDate { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Final;
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    // ---- Seller snapshot ----
    public string SellerName { get; set; } = string.Empty;
    public string SellerAddress { get; set; } = string.Empty;
    public string SellerGstin { get; set; } = string.Empty;
    public string SellerState { get; set; } = string.Empty;
    public string SellerStateCode { get; set; } = string.Empty;
    public string? SellerPhone { get; set; }
    public string? SellerEmail { get; set; }
    public string? SellerBankName { get; set; }
    public string? SellerBankAccountNumber { get; set; }
    public string? SellerBankIfsc { get; set; }
    public string? SellerBankBranch { get; set; }
    public string DeclarationText { get; set; } = string.Empty;

    // ---- Buyer snapshot ----
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerAddress { get; set; } = string.Empty;
    public string? BuyerGstin { get; set; }
    public string BuyerState { get; set; } = string.Empty;
    public string BuyerStateCode { get; set; } = string.Empty;
    public string? BuyerPhone { get; set; }

    // ---- Optional header details (as on the Tally layout) ----
    public string? BuyerOrderNumber { get; set; }
    public DateOnly? BuyerOrderDate { get; set; }
    public string? PaymentTerms { get; set; }
    public string? DispatchDocumentNumber { get; set; }
    public string? DispatchedThrough { get; set; }
    public string? Destination { get; set; }
    public string? TermsOfDelivery { get; set; }

    // ---- Tax and totals (persisted, never recalculated on display) ----
    public TaxType TaxType { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TaxableTotal { get; set; }
    public decimal CgstTotal { get; set; }
    public decimal SgstTotal { get; set; }
    public decimal IgstTotal { get; set; }
    public decimal TotalTax { get; set; }

    /// <summary>Adjustment to reach a whole-rupee grand total (between -0.50 and +0.50).</summary>
    public decimal RoundOff { get; set; }

    public decimal GrandTotal { get; set; }
    public string AmountInWords { get; set; } = string.Empty;

    public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
}
