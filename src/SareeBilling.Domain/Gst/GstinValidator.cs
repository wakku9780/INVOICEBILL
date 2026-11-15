using System.Text.RegularExpressions;

namespace SareeBilling.Domain.Gst;

public enum GstinCheck
{
    Valid,
    Empty,
    WrongLength,
    InvalidFormat,
    UnknownStateCode,
    CheckDigitMismatch
}

/// <summary>
/// Validates a regular GSTIN: 2-digit state code, 10-character PAN, entity number,
/// the letter Z, and a final check character computed from the first 14.
/// </summary>
public static partial class GstinValidator
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    public const int Length = 15;

    [GeneratedRegex("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$")]
    private static partial Regex Pattern();

    /// <summary>Upper-cases and removes spaces. Returns empty string for null.</summary>
    public static string Normalize(string? gstin) =>
        string.Concat((gstin ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    public static bool IsValid(string? gstin) => Check(gstin) == GstinCheck.Valid;

    public static GstinCheck Check(string? gstin)
    {
        var value = Normalize(gstin);

        if (value.Length == 0)
        {
            return GstinCheck.Empty;
        }

        if (value.Length != Length)
        {
            return GstinCheck.WrongLength;
        }

        if (!Pattern().IsMatch(value))
        {
            return GstinCheck.InvalidFormat;
        }

        if (IndianStates.FindByCode(value[..2]) is null)
        {
            return GstinCheck.UnknownStateCode;
        }

        return ComputeCheckCharacter(value.AsSpan(0, 14)) == value[14]
            ? GstinCheck.Valid
            : GstinCheck.CheckDigitMismatch;
    }

    /// <summary>A short message suitable for showing under a GSTIN box.</summary>
    public static string Describe(GstinCheck check, string? gstin) => check switch
    {
        GstinCheck.Valid => "Valid GSTIN",
        GstinCheck.Empty => "GSTIN is empty",
        GstinCheck.WrongLength => $"GSTIN must have 15 characters (entered {Normalize(gstin).Length})",
        GstinCheck.InvalidFormat => "GSTIN format is not correct",
        GstinCheck.UnknownStateCode => "GSTIN starts with an unknown state code",
        GstinCheck.CheckDigitMismatch => "GSTIN looks mistyped (last character does not match) - please re-check",
        _ => "GSTIN is not valid"
    };

    /// <summary>Standard GSTN mod-36 check-character algorithm.</summary>
    public static char ComputeCheckCharacter(ReadOnlySpan<char> first14)
    {
        if (first14.Length != 14)
        {
            throw new ArgumentException("Exactly 14 characters are required.", nameof(first14));
        }

        var sum = 0;
        for (var i = 0; i < first14.Length; i++)
        {
            var value = Alphabet.IndexOf(first14[i]);
            if (value < 0)
            {
                throw new ArgumentException("GSTIN may contain only digits and capital letters.", nameof(first14));
            }

            var product = value * (i % 2 == 0 ? 1 : 2);
            sum += product / 36 + product % 36;
        }

        return Alphabet[(36 - sum % 36) % 36];
    }
}
