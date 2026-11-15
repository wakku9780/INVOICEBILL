namespace SareeBilling.Domain.Entities;

/// <summary>
/// Tracks the last invoice number issued in each financial year.
/// Numbering restarts every 1 April. The starting number for a year can be
/// set (e.g. to continue after the last Tally invoice) by seeding this row.
/// </summary>
public class InvoiceSequence
{
    /// <summary>Financial year label, e.g. "2026-27". Primary key.</summary>
    public string FinancialYear { get; set; } = string.Empty;

    public int LastNumber { get; set; }
}
