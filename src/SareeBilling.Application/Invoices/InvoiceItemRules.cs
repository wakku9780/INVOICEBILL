using System.Text.RegularExpressions;
using SareeBilling.Application.Common;

namespace SareeBilling.Application.Invoices;

/// <summary>
/// Rules for a single invoice line. Public so the invoice screen can check a line
/// the moment it is added, using exactly the same rules as the final save.
/// </summary>
public static partial class InvoiceItemRules
{
    public const decimal MaxGstRate = 40m;
    public const int MaxItemsPerInvoice = 100;

    public static IReadOnlyList<string> CommonUnits { get; } = ["Qty", "Pcs", "Mtr", "Set"];
    public static IReadOnlyList<decimal> CommonGstRates { get; } = [5m, 12m, 18m, 3m, 0m, 28m, 40m];

    /// <summary>HSN (goods) has 4, 6 or 8 digits; SAC (services) has 6.</summary>
    [GeneratedRegex("^([0-9]{4}|[0-9]{6}|[0-9]{8})$")]
    private static partial Regex HsnPattern();

    public static List<string> Validate(InvoiceItemInput item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var errors = new List<string>();

        InputRules.Required(errors, item.Description?.Trim() ?? string.Empty, "Description", 300);

        var hsn = item.HsnCode?.Trim() ?? string.Empty;
        if (hsn.Length == 0)
        {
            errors.Add("HSN code is required.");
        }
        else if (!HsnPattern().IsMatch(hsn))
        {
            errors.Add("HSN code must be 4, 6 or 8 digits (for example 5007).");
        }

        if (item.Quantity <= 0)
        {
            errors.Add("Quantity must be more than 0.");
        }
        else if (decimal.Round(item.Quantity, 3) != item.Quantity)
        {
            errors.Add("Quantity can have at most 3 decimal places.");
        }

        InputRules.Required(errors, item.Unit?.Trim() ?? string.Empty, "Unit", 10);

        if (item.Rate <= 0)
        {
            errors.Add("Rate must be more than 0.");
        }
        else if (decimal.Round(item.Rate, 2) != item.Rate)
        {
            errors.Add("Rate can have at most 2 decimal places (paise).");
        }

        if (item.GstRate < 0 || item.GstRate > MaxGstRate || decimal.Round(item.GstRate, 2) != item.GstRate)
        {
            errors.Add($"GST % must be between 0 and {MaxGstRate:0}.");
        }

        return errors;
    }
}
