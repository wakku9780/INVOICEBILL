using SareeBilling.Domain.Common;

namespace SareeBilling.Tests.Domain;

public class FormattingTests
{
    [Theory]
    [InlineData("350595", "3,50,595.00")]
    [InlineData("333900", "3,33,900.00")]
    [InlineData("16695", "16,695.00")]
    [InlineData("12345678.9", "1,23,45,678.90")]
    [InlineData("0", "0.00")]
    [InlineData("-0.06", "-0.06")]
    public void Amounts_use_indian_grouping(string value, string expected)
    {
        Assert.Equal(expected, IndianFormat.Amount(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Quantity_and_percent_drop_trailing_zeros()
    {
        Assert.Equal("53", IndianFormat.Quantity(53.000m));
        Assert.Equal("2.5", IndianFormat.Quantity(2.500m));
        Assert.Equal("5", IndianFormat.Percent(5.00m));
        Assert.Equal("2.5", IndianFormat.Percent(2.50m));
    }

    [Fact]
    public void Invoice_number_format()
    {
        Assert.Equal("161", InvoiceNumber.Format(161));
        Assert.Throws<ArgumentOutOfRangeException>(() => InvoiceNumber.Format(0));
    }

    [Fact]
    public void Address_skips_empty_line_and_pincode()
    {
        var nl = Environment.NewLine;
        Assert.Equal($"24 Bhavani Peth{nl}Opp. Canara Bank{nl}Pune - 411002",
            PostalAddress.Format("24 Bhavani Peth", "Opp. Canara Bank", "Pune", "411002"));
        Assert.Equal($"Ring Road{nl}Surat", PostalAddress.Format("Ring Road", " ", "Surat", null));
    }
}
