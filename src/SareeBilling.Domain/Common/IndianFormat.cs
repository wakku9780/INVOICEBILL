using System.Globalization;

namespace SareeBilling.Domain.Common;

/// <summary>
/// Indian digit grouping (3,50,595.00 and 1,23,45,678.90), independent of the
/// Windows region setting of the computer the app runs on.
/// </summary>
public static class IndianFormat
{
    private static readonly CultureInfo Culture = CreateCulture();

    /// <example>350595m → "3,50,595.00"</example>
    public static string Amount(decimal value) => value.ToString("#,##0.00", Culture);

    /// <summary>Up to 3 decimals, trailing zeros removed. 53m → "53", 2.5m → "2.5".</summary>
    public static string Quantity(decimal value) => value.ToString("#,##0.###", Culture);

    /// <summary>GST percentage without trailing zeros. 5.00m → "5", 2.5m → "2.5".</summary>
    public static string Percent(decimal value) => value.ToString("0.##", Culture);

    private static CultureInfo CreateCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberGroupSizes = [3, 2];
        return CultureInfo.ReadOnly(culture);
    }
}
