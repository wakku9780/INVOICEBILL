using SareeBilling.Domain.Enums;
using SareeBilling.Domain.Gst;

namespace SareeBilling.Tests.Domain;

public class GstCalculatorTests
{
    private const string UttarPradesh = "09";
    private const string Maharashtra = "27";

    [Fact]
    public void Reference_invoice_inter_state_igst_matches_printed_totals()
    {
        var result = GstCalculator.Calculate([new GstLineInput(53m, 6300m, 5m)], UttarPradesh, Maharashtra);

        Assert.Equal(TaxType.InterState, result.TaxType);
        Assert.Equal(333900.00m, result.TaxableTotal);
        Assert.Equal(16695.00m, result.IgstTotal);
        Assert.Equal(0m, result.CgstTotal);
        Assert.Equal(0m, result.SgstTotal);
        Assert.Equal(0m, result.RoundOff);
        Assert.Equal(350595.00m, result.GrandTotal);
        Assert.Equal(53m, result.TotalQuantity);
    }

    [Fact]
    public void Same_state_splits_into_equal_cgst_and_sgst_and_rounds_to_rupee()
    {
        var result = GstCalculator.Calculate([new GstLineInput(3m, 1234.50m, 5m)], UttarPradesh, UttarPradesh);

        Assert.Equal(TaxType.IntraState, result.TaxType);
        Assert.Equal(3703.50m, result.TaxableTotal);
        Assert.Equal(92.59m, result.CgstTotal);   // 92.5875 rounded
        Assert.Equal(92.59m, result.SgstTotal);
        Assert.Equal(0m, result.IgstTotal);
        Assert.Equal(2.5m, result.Lines[0].CgstRate);
        Assert.Equal(0.32m, result.RoundOff);     // 3888.68 → 3889
        Assert.Equal(3889m, result.GrandTotal);
    }

    [Fact]
    public void Half_paisa_rounds_up()
    {
        var line = GstCalculator.CalculateLine(new GstLineInput(1m, 101m, 5m), TaxType.IntraState);

        Assert.Equal(2.53m, line.CgstAmount); // 2.525
        Assert.Equal(2.53m, line.SgstAmount);
    }

    [Fact]
    public void Fifty_paise_round_off_goes_up()
    {
        var result = GstCalculator.Calculate([new GstLineInput(1m, 10m, 5m)], UttarPradesh, Maharashtra);

        Assert.Equal(0.50m, result.RoundOff);
        Assert.Equal(11m, result.GrandTotal);
    }

    [Fact]
    public void Round_off_can_be_negative()
    {
        var result = GstCalculator.Calculate([new GstLineInput(1m, 101m, 5m)], UttarPradesh, UttarPradesh);

        Assert.Equal(-0.06m, result.RoundOff); // 106.06 → 106
        Assert.Equal(106m, result.GrandTotal);
    }

    [Fact]
    public void Fractional_quantity_taxable_amount_is_rounded_to_paise()
    {
        var line = GstCalculator.CalculateLine(new GstLineInput(2.5m, 333.33m, 5m), TaxType.InterState);

        Assert.Equal(833.33m, line.TaxableAmount); // 833.325
        Assert.Equal(41.67m, line.IgstAmount);     // 41.6665
        Assert.Equal(875.00m, line.LineTotal);
    }

    [Fact]
    public void Different_gst_rates_per_line_are_summed()
    {
        var result = GstCalculator.Calculate(
            [new GstLineInput(1m, 1000m, 5m), new GstLineInput(2m, 500m, 18m)],
            UttarPradesh, UttarPradesh);

        Assert.Equal(2000m, result.TaxableTotal);
        Assert.Equal(115m, result.CgstTotal);
        Assert.Equal(115m, result.SgstTotal);
        Assert.Equal(230m, result.TotalTax);
        Assert.Equal(2230m, result.GrandTotal);
    }

    [Theory]
    [InlineData(0, 100, 5)]
    [InlineData(-1, 100, 5)]
    [InlineData(1, -1, 5)]
    [InlineData(1, 100, -5)]
    [InlineData(1, 100, 101)]
    public void Invalid_inputs_are_rejected(decimal quantity, decimal rate, decimal gstRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GstCalculator.CalculateLine(new GstLineInput(quantity, rate, gstRate), TaxType.InterState));
    }

    [Fact]
    public void Invoice_without_items_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => GstCalculator.Calculate([], UttarPradesh, Maharashtra));
    }
}
