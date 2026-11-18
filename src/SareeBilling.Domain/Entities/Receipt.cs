using SareeBilling.Domain.Common;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Domain.Entities;

/// <summary>Money received from a customer (receipt voucher). Never edited; only cancelled.</summary>
public class Receipt : AuditableEntity
{
    /// <summary>"R-" + <see cref="SequenceNumber"/>, e.g. "R-12".</summary>
    public string ReceiptNumber { get; set; } = string.Empty;

    public int SequenceNumber { get; set; }

    public DateOnly ReceiptDate { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>Customer name when the receipt was made (kept even if the customer is renamed).</summary>
    public string CustomerName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public PaymentMode Mode { get; set; }

    /// <summary>UTR, UPI reference or cheque number.</summary>
    public string? Reference { get; set; }

    public string? Narration { get; set; }

    public VoucherStatus Status { get; set; } = VoucherStatus.Posted;

    public DateTime? CancelledAtUtc { get; set; }

    public string? CancellationReason { get; set; }
}
