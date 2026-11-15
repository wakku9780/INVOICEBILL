namespace SareeBilling.Domain.Common;

/// <summary>Indian financial year: 1 April to 31 March.</summary>
public static class FinancialYear
{
    /// <returns>Label such as "2026-27" for any date from 1 Apr 2026 to 31 Mar 2027.</returns>
    public static string For(DateOnly date)
    {
        var startYear = date.Month >= 4 ? date.Year : date.Year - 1;
        return $"{startYear}-{(startYear + 1) % 100:00}";
    }
}
