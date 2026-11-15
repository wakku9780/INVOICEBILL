namespace SareeBilling.Domain.Common;

public static class InvoiceNumber
{
    /// <example>("JAS", "2026-27", 161) → "JAS/2026-27/161"</example>
    public static string Format(string prefix, string financialYear, int sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(financialYear);
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);

        return $"{prefix}/{financialYear}/{sequence}";
    }
}
