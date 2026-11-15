namespace SareeBilling.Domain.Common;

/// <summary>
/// Single place for rounding rules. Half-paise round up (away from zero),
/// matching how Indian invoices and Tally round amounts.
/// </summary>
public static class Money
{
    public static decimal Round2(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal RoundToRupee(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
