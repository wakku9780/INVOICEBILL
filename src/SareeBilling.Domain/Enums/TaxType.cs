namespace SareeBilling.Domain.Enums;

/// <summary>
/// Decided by comparing the seller's and buyer's state codes.
/// </summary>
public enum TaxType
{
    /// <summary>Same state: CGST + SGST.</summary>
    IntraState = 1,

    /// <summary>Different states: IGST.</summary>
    InterState = 2
}
