using SareeBilling.Domain.Entities;

namespace SareeBilling.Domain.Gst;

public sealed record HsnTaxSummaryLine(
    string HsnCode,
    decimal GstRate,
    decimal TaxableAmount,
    decimal CgstRate,
    decimal CgstAmount,
    decimal SgstRate,
    decimal SgstAmount,
    decimal IgstRate,
    decimal IgstAmount)
{
    public decimal TotalTax => CgstAmount + SgstAmount + IgstAmount;
}

/// <summary>
/// HSN-wise tax summary printed under the item table (as in Tally).
/// Built from the stored line amounts, so it always agrees with the invoice totals.
/// </summary>
public static class HsnTaxSummary
{
    public static IReadOnlyList<HsnTaxSummaryLine> From(IEnumerable<InvoiceItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return Summarise(items.Select(i => new HsnTaxSummaryLine(
            i.HsnCode, i.GstRate, i.TaxableAmount, i.CgstRate, i.CgstAmount, i.SgstRate, i.SgstAmount, i.IgstRate, i.IgstAmount)));
    }

    public static IReadOnlyList<HsnTaxSummaryLine> From(IEnumerable<CreditNoteItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return Summarise(items.Select(i => new HsnTaxSummaryLine(
            i.HsnCode, i.GstRate, i.TaxableAmount, i.CgstRate, i.CgstAmount, i.SgstRate, i.SgstAmount, i.IgstRate, i.IgstAmount)));
    }

    private static IReadOnlyList<HsnTaxSummaryLine> Summarise(IEnumerable<HsnTaxSummaryLine> items)
    {
        return items
            .GroupBy(i => (i.HsnCode, i.GstRate))
            .OrderBy(g => g.Key.HsnCode, StringComparer.Ordinal)
            .ThenBy(g => g.Key.GstRate)
            .Select(g => new HsnTaxSummaryLine(
                g.Key.HsnCode,
                g.Key.GstRate,
                g.Sum(i => i.TaxableAmount),
                g.First().CgstRate,
                g.Sum(i => i.CgstAmount),
                g.First().SgstRate,
                g.Sum(i => i.SgstAmount),
                g.First().IgstRate,
                g.Sum(i => i.IgstAmount)))
            .ToList();
    }
}
