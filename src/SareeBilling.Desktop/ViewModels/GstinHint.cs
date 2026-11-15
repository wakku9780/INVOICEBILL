using SareeBilling.Domain.Gst;

namespace SareeBilling.Desktop.ViewModels;

/// <summary>
/// Live feedback under a GSTIN box while typing, and the state the GSTIN belongs to.
/// </summary>
public sealed record GstinHint(string Message, bool IsValid, string? StateCode)
{
    public static GstinHint For(string? gstin)
    {
        var check = GstinValidator.Check(gstin);
        return check switch
        {
            GstinCheck.Empty => new GstinHint(string.Empty, false, null),
            GstinCheck.Valid => new GstinHint(
                $"✓ Valid GSTIN ({IndianStates.FindByCode(GstinValidator.Normalize(gstin)[..2])?.Name})",
                true,
                GstinValidator.Normalize(gstin)[..2]),
            _ => new GstinHint(GstinValidator.Describe(check, gstin), false, null)
        };
    }
}
