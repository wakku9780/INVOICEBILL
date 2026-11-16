using System.Globalization;

namespace SareeBilling.Domain.Common;

/// <summary>
/// Invoice numbers are plain running numbers (1, 2, 3 …) that never restart,
/// so every number is unique for all time.
/// </summary>
public static class InvoiceNumber
{
    /// <example>161 → "161"</example>
    public static string Format(int sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        return sequence.ToString(CultureInfo.InvariantCulture);
    }
}
