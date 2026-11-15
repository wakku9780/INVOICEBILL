namespace SareeBilling.Domain.Enums;

/// <summary>
/// A saved invoice is final. Mistakes are handled by cancelling it;
/// a cancelled invoice stays in history and its number is never reused.
/// </summary>
public enum InvoiceStatus
{
    Final = 1,
    Cancelled = 2
}
