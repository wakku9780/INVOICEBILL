using System.Text.RegularExpressions;
using SareeBilling.Domain.Gst;

namespace SareeBilling.Application.Common;

/// <summary>Reusable field checks. Each adds a friendly message to the error list.</summary>
internal static partial class InputRules
{
    [GeneratedRegex("^[1-9][0-9]{5}$")]
    private static partial Regex PincodePattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex("^[A-Z]{4}0[A-Z0-9]{6}$")]
    private static partial Regex IfscPattern();

    public static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string CleanRequired(string? value) => value?.Trim() ?? string.Empty;

    public static void Required(List<string> errors, string value, string field, int maxLength)
    {
        if (value.Length == 0)
        {
            errors.Add($"{field} is required.");
        }
        else
        {
            MaxLength(errors, value, field, maxLength);
        }
    }

    public static void MaxLength(List<string> errors, string? value, string field, int maxLength)
    {
        if (value is not null && value.Length > maxLength)
        {
            errors.Add($"{field} can be at most {maxLength} characters.");
        }
    }

    public static void Pincode(List<string> errors, string? value)
    {
        if (value is not null && !PincodePattern().IsMatch(value))
        {
            errors.Add("Pincode must be 6 digits.");
        }
    }

    public static void Email(List<string> errors, string? value)
    {
        if (value is not null && !EmailPattern().IsMatch(value))
        {
            errors.Add("Email address is not valid.");
        }
    }

    public static void Ifsc(List<string> errors, string? value)
    {
        if (value is not null && !IfscPattern().IsMatch(value))
        {
            errors.Add("IFSC code must be 11 characters, like SBIN0001234.");
        }
    }

    /// <returns>The matching state, or null after adding an error.</returns>
    public static IndianState? State(List<string> errors, string? stateCode)
    {
        var state = IndianStates.FindByCode(stateCode);
        if (state is null)
        {
            errors.Add("Please select the State.");
        }

        return state;
    }

    /// <summary>Checks the GSTIN itself and that it belongs to the selected state.</summary>
    public static void Gstin(List<string> errors, string? gstin, IndianState? state)
    {
        if (gstin is null)
        {
            return;
        }

        var check = GstinValidator.Check(gstin);
        if (check != GstinCheck.Valid)
        {
            errors.Add(GstinValidator.Describe(check, gstin) + ".");
            return;
        }

        if (state is not null && gstin[..2] != state.Code)
        {
            errors.Add($"GSTIN starts with {gstin[..2]}, but the selected state is {state.DisplayName}. " +
                       "The first two digits of a GSTIN are the state code.");
        }
    }
}
