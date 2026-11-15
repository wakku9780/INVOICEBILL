using SareeBilling.Domain.Common;

namespace SareeBilling.Tests.Domain;

public class AmountInWordsTests
{
    [Theory]
    [InlineData("350595", "Rs. Three Lakh Fifty Thousand Five Hundred Ninety Five Only")]
    [InlineData("0", "Rs. Zero Only")]
    [InlineData("19", "Rs. Nineteen Only")]
    [InlineData("90", "Rs. Ninety Only")]
    [InlineData("100", "Rs. One Hundred Only")]
    [InlineData("1000001", "Rs. Ten Lakh One Only")]
    [InlineData("12345678", "Rs. One Crore Twenty Three Lakh Forty Five Thousand Six Hundred Seventy Eight Only")]
    [InlineData("1234567890", "Rs. One Hundred Twenty Three Crore Forty Five Lakh Sixty Seven Thousand Eight Hundred Ninety Only")]
    [InlineData("10.50", "Rs. Ten and Fifty Paise Only")]
    [InlineData("0.05", "Rs. Zero and Five Paise Only")]
    public void Converts_using_indian_numbering(string amount, string expected)
    {
        Assert.Equal(expected, AmountInWords.ToRupees(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Negative_amount_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AmountInWords.ToRupees(-1m));
    }
}
