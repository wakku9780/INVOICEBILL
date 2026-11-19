using SareeBilling.Domain.Common;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Domain.Gst;

public sealed record GstLineInput(decimal Quantity, decimal Rate, decimal GstRate);

public sealed record GstLineResult(
    decimal TaxableAmount,
    decimal GstRate,
    decimal CgstRate,
    decimal CgstAmount,
    decimal SgstRate,
    decimal SgstAmount,
    decimal IgstRate,
    decimal IgstAmount)
{
    public decimal TotalTax => CgstAmount + SgstAmount + IgstAmount;
    public decimal LineTotal => TaxableAmount + TotalTax;
}

public sealed record GstInvoiceResult(
    TaxType TaxType,
    IReadOnlyList<GstLineResult> Lines,
    decimal TotalQuantity,
    decimal TaxableTotal,
    decimal CgstTotal,
    decimal SgstTotal,
    decimal IgstTotal,
    decimal TotalTax,
    decimal RoundOff,
    decimal GrandTotal);

/// <summary>
/// All GST arithmetic in one place.
/// <list type="bullet">
/// <item>Taxable = Quantity × Rate, rounded to paise.</item>
/// <item>Same state → CGST and SGST at half the rate each, each rounded separately (so they are always equal).</item>
/// <item>Different states → IGST at the full rate.</item>
/// <item>Tax is calculated per line; totals are sums of the lines.</item>
/// <item>The grand total is rounded to the nearest rupee, with the difference shown as Round Off.</item>
/// </list>
/// </summary>
public static class GstCalculator
{
    public static TaxType DetermineTaxType(string sellerStateCode, string buyerStateCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sellerStateCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(buyerStateCode);

        return string.Equals(sellerStateCode.Trim(), buyerStateCode.Trim(), StringComparison.Ordinal)
            ? TaxType.IntraState
            : TaxType.InterState;
    }

    public static GstLineResult CalculateLine(GstLineInput line, TaxType taxType)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(line), "Quantity must be greater than zero.");
        }

        if (line.Rate < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(line), "Rate cannot be negative.");
        }

        if (line.GstRate is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(line), "GST rate must be between 0 and 100.");
        }

        return CalculateOnTaxable(Money.Round2(line.Quantity * line.Rate), line.GstRate, taxType);
    }

    /// <summary>GST on an already-known taxable amount (used by credit notes), rounded exactly as invoice lines are.</summary>
    public static GstLineResult CalculateOnTaxable(decimal taxable, decimal gstRate, TaxType taxType)
    {
        if (taxable < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(taxable), "Taxable amount cannot be negative.");
        }

        if (gstRate is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(gstRate), "GST rate must be between 0 and 100.");
        }

        if (taxType == TaxType.IntraState)
        {
            var halfRate = gstRate / 2;
            var half = Money.Round2(taxable * halfRate / 100);
            return new GstLineResult(taxable, gstRate, halfRate, half, halfRate, half, 0m, 0m);
        }

        var igst = Money.Round2(taxable * gstRate / 100);
        return new GstLineResult(taxable, gstRate, 0m, 0m, 0m, 0m, gstRate, igst);
    }

    public static GstInvoiceResult Calculate(
        IReadOnlyCollection<GstLineInput> lines,
        string sellerStateCode,
        string buyerStateCode)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new ArgumentException("An invoice needs at least one item.", nameof(lines));
        }

        var taxType = DetermineTaxType(sellerStateCode, buyerStateCode);
        var results = lines.Select(l => CalculateLine(l, taxType)).ToList();

        var taxableTotal = results.Sum(r => r.TaxableAmount);
        var cgst = results.Sum(r => r.CgstAmount);
        var sgst = results.Sum(r => r.SgstAmount);
        var igst = results.Sum(r => r.IgstAmount);
        var totalTax = cgst + sgst + igst;

        var beforeRounding = taxableTotal + totalTax;
        var grandTotal = Money.RoundToRupee(beforeRounding);

        return new GstInvoiceResult(
            taxType,
            results,
            lines.Sum(l => l.Quantity),
            taxableTotal,
            cgst,
            sgst,
            igst,
            totalTax,
            RoundOff: grandTotal - beforeRounding,
            grandTotal);
    }
}
