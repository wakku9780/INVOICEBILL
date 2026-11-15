namespace SareeBilling.Domain.Common;

/// <summary>
/// Converts amounts to words using the Indian numbering system (Thousand, Lakh, Crore),
/// e.g. 350595 → "Rs. Three Lakh Fifty Thousand Five Hundred Ninety Five Only".
/// </summary>
public static class AmountInWords
{
    private static readonly string[] Ones =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
        "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen",
        "Seventeen", "Eighteen", "Nineteen"
    ];

    private static readonly string[] Tens =
    [
        "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"
    ];

    public static string ToRupees(decimal amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        amount = Money.Round2(amount);
        var rupees = (long)decimal.Truncate(amount);
        var paise = (int)((amount - rupees) * 100);

        var words = "Rs. " + NumberToWords(rupees);
        if (paise > 0)
        {
            words += " and " + NumberToWords(paise) + " Paise";
        }

        return words + " Only";
    }

    public static string NumberToWords(long number)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(number);

        if (number == 0)
        {
            return Ones[0];
        }

        var parts = new List<string>(6);

        var crore = number / 10_000_000;
        if (crore > 0)
        {
            // Amounts of 100 crore and more are read as "One Hundred Twenty Crore".
            parts.Add(NumberToWords(crore) + " Crore");
        }

        AddGroup(parts, number / 100_000 % 100, "Lakh");
        AddGroup(parts, number / 1_000 % 100, "Thousand");
        AddGroup(parts, number / 100 % 10, "Hundred");

        var lastTwo = number % 100;
        if (lastTwo > 0)
        {
            parts.Add(TwoDigits(lastTwo));
        }

        return string.Join(" ", parts);
    }

    private static void AddGroup(List<string> parts, long value, string scale)
    {
        if (value > 0)
        {
            parts.Add(TwoDigits(value) + " " + scale);
        }
    }

    private static string TwoDigits(long value) =>
        value < 20
            ? Ones[value]
            : value % 10 == 0
                ? Tens[value / 10]
                : Tens[value / 10] + " " + Ones[value % 10];
}
