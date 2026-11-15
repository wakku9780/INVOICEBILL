using SareeBilling.Domain.Common;
using SareeBilling.Domain.Gst;

namespace SareeBilling.Tests.Domain;

public class GstinValidatorTests
{
    [Theory]
    [InlineData("09ACSPH2858M1ZT")] // seller on the reference invoice
    [InlineData("27AAOFK4239P1ZK")] // buyer on the reference invoice
    [InlineData(" 27aaofk4239p1zk ")] // typed in lower case with spaces
    public void Accepts_valid_gstins(string gstin)
    {
        Assert.Equal(GstinCheck.Valid, GstinValidator.Check(gstin));
    }

    [Theory]
    [InlineData("", GstinCheck.Empty)]
    [InlineData(null, GstinCheck.Empty)]
    [InlineData("27AAOFK4239P1Z", GstinCheck.WrongLength)]
    [InlineData("27AAOFK4239P1XK", GstinCheck.InvalidFormat)]
    [InlineData("2AAAOFK4239P1ZK", GstinCheck.InvalidFormat)]
    [InlineData("99AAOFK4239P1ZK", GstinCheck.UnknownStateCode)]
    [InlineData("27AAOFK4239P1ZA", GstinCheck.CheckDigitMismatch)]
    [InlineData("27AAOFK4293P1ZK", GstinCheck.CheckDigitMismatch)] // two digits swapped
    public void Rejects_invalid_gstins(string? gstin, GstinCheck expected)
    {
        Assert.Equal(expected, GstinValidator.Check(gstin));
    }

    [Fact]
    public void State_list_contains_reference_states()
    {
        Assert.Equal("Uttar Pradesh", IndianStates.FindByCode("09")?.Name);
        Assert.Equal("Maharashtra", IndianStates.FindByCode("27")?.Name);
        Assert.Null(IndianStates.FindByCode("25"));
    }
}

public class FinancialYearTests
{
    [Theory]
    [InlineData(2026, 11, 1, "2026-27")]
    [InlineData(2027, 3, 31, "2026-27")]
    [InlineData(2027, 4, 1, "2027-28")]
    [InlineData(2099, 12, 31, "2099-00")]
    public void Uses_april_to_march_years(int year, int month, int day, string expected)
    {
        Assert.Equal(expected, FinancialYear.For(new DateOnly(year, month, day)));
    }
}
