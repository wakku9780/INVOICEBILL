using SareeBilling.Domain.Common;

namespace SareeBilling.Domain.Entities;

/// <summary>
/// Bill-wise settlement: part of a credit (a receipt, an opening advance or a credit note) used to pay part
/// of a bill (an invoice or an opening bill). Exactly one source and one target are set.
/// Rows are removed when either side is cancelled; the money then becomes advance again
/// (or the bill pending again).
/// </summary>
public class BillAllocation : AuditableEntity
{
    public int CustomerId { get; set; }

    /// <summary>The day the money was matched (receipt date, or the day an advance was adjusted).</summary>
    public DateOnly AllocatedOn { get; set; }

    public int? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }

    /// <summary>An opening entry on the Credit side (old advance).</summary>
    public int? OpeningCreditId { get; set; }
    public OpeningBill? OpeningCredit { get; set; }

    /// <summary>A credit note (sales return or value reduction).</summary>
    public int? CreditNoteId { get; set; }
    public CreditNote? CreditNote { get; set; }

    public int? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    /// <summary>An opening entry on the Debit side (old unpaid bill).</summary>
    public int? OpeningBillId { get; set; }
    public OpeningBill? OpeningBill { get; set; }

    public decimal Amount { get; set; }
}
