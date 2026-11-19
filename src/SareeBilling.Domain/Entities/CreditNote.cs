using SareeBilling.Domain.Common;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Domain.Entities;

/// <summary>
/// A GST credit note issued against one sales invoice: goods returned, or a later reduction in
/// value (rate difference / discount). Uses the invoice's tax type and GST rates. Every amount is
/// stored, so a reprint always matches. Never edited; only cancelled.
/// </summary>
public class CreditNote : AuditableEntity
{
    /// <summary>"CN-" + <see cref="SequenceNumber"/>, e.g. "CN-3".</summary>
    public string CreditNoteNumber { get; set; } = string.Empty;

    public int SequenceNumber { get; set; }

    public DateOnly CreditNoteDate { get; set; }

    public CreditNoteKind Kind { get; set; }

    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    /// <summary>The original invoice's number and date, as printed on the credit note.</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    public DateOnly InvoiceDate { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>Why the credit note was issued, e.g. "5 pieces returned damaged".</summary>
    public string Reason { get; set; } = string.Empty;

    // ---- Seller snapshot (company details when the credit note was made) ----
    public string SellerName { get; set; } = string.Empty;
    public string SellerAddress { get; set; } = string.Empty;
    public string SellerGstin { get; set; } = string.Empty;
    public string SellerState { get; set; } = string.Empty;
    public string SellerStateCode { get; set; } = string.Empty;
    public string? SellerPhone { get; set; }
    public string? SellerEmail { get; set; }

    // ---- Buyer snapshot (copied from the original invoice) ----
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerAddress { get; set; } = string.Empty;
    public string? BuyerGstin { get; set; }
    public string BuyerState { get; set; } = string.Empty;
    public string BuyerStateCode { get; set; } = string.Empty;
    public string? BuyerPhone { get; set; }

    // ---- Tax and totals (persisted, never recalculated on display) ----
    public TaxType TaxType { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TaxableTotal { get; set; }
    public decimal CgstTotal { get; set; }
    public decimal SgstTotal { get; set; }
    public decimal IgstTotal { get; set; }
    public decimal TotalTax { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }
    public string AmountInWords { get; set; } = string.Empty;

    public VoucherStatus Status { get; set; } = VoucherStatus.Posted;
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public ICollection<CreditNoteItem> Items { get; set; } = new List<CreditNoteItem>();
}

/// <summary>
/// One line of a credit note, always against one line of the original invoice.
/// For goods returned, <see cref="Quantity"/> is the quantity returned; for an amount-only
/// credit note it is 0 and only <see cref="TaxableAmount"/> is reduced.
/// </summary>
public class CreditNoteItem
{
    public int Id { get; set; }

    public int CreditNoteId { get; set; }
    public CreditNote CreditNote { get; set; } = null!;

    public int LineNumber { get; set; }

    public int InvoiceItemId { get; set; }
    public InvoiceItem InvoiceItem { get; set; } = null!;

    public string Description { get; set; } = string.Empty;
    public string HsnCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GstRate { get; set; }
    public decimal CgstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstRate { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstRate { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal LineTotal { get; set; }
}
