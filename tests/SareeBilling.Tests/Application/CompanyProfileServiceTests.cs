using SareeBilling.Application.Common;
using SareeBilling.Application.CompanyProfiles;

namespace SareeBilling.Tests.Application;

public sealed class CompanyProfileServiceTests : IAsyncLifetime
{
    private TestHost _host = null!;
    private ICompanyProfileService Service => _host.Get<ICompanyProfileService>();

    public async Task InitializeAsync() => _host = await TestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static CompanyProfileDto JaSarees() => new()
    {
        Name = "J A SAREES",
        AddressLine1 = "Purani Basti Lalkunwan",
        AddressLine2 = "Mubarakpur",
        City = "Azamgarh",
        Pincode = "276404",
        StateCode = "09",
        Gstin = "09ACSPH2858M1ZT",
        Phone = "9000000003",
        BankIfsc = "sbin0001234",
        DeclarationText = "We declare that this invoice shows the actual price."
    };

    [Fact]
    public async Task Returns_null_before_setup()
    {
        Assert.Null(await Service.GetAsync());
    }

    [Fact]
    public async Task Saves_then_updates_single_profile()
    {
        await Service.SaveAsync(JaSarees());
        await Service.SaveAsync(JaSarees() with { Phone = "9000000004" });

        var saved = await Service.GetAsync();
        Assert.NotNull(saved);
        Assert.Equal("9000000004", saved.Phone);
        Assert.Equal("Uttar Pradesh", saved.State);
        Assert.Equal("SBIN0001234", saved.BankIfsc);
    }

    [Fact]
    public async Task Gstin_is_required_for_company()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service.SaveAsync(JaSarees() with { Gstin = "" }));

        Assert.Contains("GSTIN is required for your company.", ex.Errors);
    }

    [Fact]
    public async Task Logo_must_be_png_or_jpeg()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service.SaveAsync(JaSarees() with { Logo = [1, 2, 3, 4] }));

        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        await Service.SaveAsync(JaSarees() with { Logo = png });
        Assert.Equal(png, (await Service.GetAsync())!.Logo);
    }
}
