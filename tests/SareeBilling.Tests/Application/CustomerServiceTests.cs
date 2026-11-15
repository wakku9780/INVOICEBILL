using Microsoft.EntityFrameworkCore;
using SareeBilling.Application.Common;
using SareeBilling.Application.Customers;
using SareeBilling.Domain.Entities;
using SareeBilling.Domain.Enums;
using SareeBilling.Infrastructure.Persistence;

namespace SareeBilling.Tests.Application;

public sealed class CustomerServiceTests : IAsyncLifetime
{
    private TestHost _host = null!;
    private ICustomerService Service => _host.Get<ICustomerService>();

    public async Task InitializeAsync() => _host = await TestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static CustomerDto Katariya(string? gstin = "27AAOFK4239P1ZK", string stateCode = "27") => new()
    {
        Name = "  Katariya Textiles ",
        AddressLine1 = "24 Bhavani Peth Palki Chowk",
        AddressLine2 = "Opp. Canara Bank",
        City = "Pune",
        Pincode = "411002",
        StateCode = stateCode,
        Gstin = gstin,
        Phone = "9000000001 / 9000000002"
    };

    [Fact]
    public async Task Saves_trimmed_customer_with_state_name_from_code_and_uppercase_gstin()
    {
        var id = await Service.SaveAsync(Katariya(gstin: "27aaofk4239p1zk"));

        var saved = await Service.GetAsync(id);
        Assert.NotNull(saved);
        Assert.Equal("Katariya Textiles", saved.Name);
        Assert.Equal("Maharashtra", saved.State);
        Assert.Equal("27AAOFK4239P1ZK", saved.Gstin);
    }

    [Fact]
    public async Task Unregistered_customer_without_gstin_is_allowed()
    {
        var id = await Service.SaveAsync(Katariya(gstin: "  "));

        Assert.Null((await Service.GetAsync(id))!.Gstin);
    }

    [Fact]
    public async Task Gstin_from_another_state_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service.SaveAsync(Katariya(stateCode: "09")));

        Assert.Contains(ex.Errors, e => e.Contains("state code"));
    }

    [Fact]
    public async Task Missing_required_fields_are_all_reported()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service.SaveAsync(new CustomerDto()));

        Assert.Contains("Customer name is required.", ex.Errors);
        Assert.Contains("Address line 1 is required.", ex.Errors);
        Assert.Contains("City is required.", ex.Errors);
        Assert.Contains("Please select the State.", ex.Errors);
    }

    [Fact]
    public async Task Duplicate_gstin_is_rejected_but_editing_same_customer_is_allowed()
    {
        var id = await Service.SaveAsync(Katariya());

        await Assert.ThrowsAsync<ValidationException>(() => Service.SaveAsync(Katariya()));

        await Service.SaveAsync(Katariya() with { Id = id, City = "Pune City" });
        Assert.Equal("Pune City", (await Service.GetAsync(id))!.City);
    }

    [Fact]
    public async Task Search_is_case_insensitive_and_matches_gstin_and_city()
    {
        await Service.SaveAsync(Katariya());
        await Service.SaveAsync(Katariya(gstin: null) with { Name = "Rani Sarees", City = "Surat", StateCode = "24" });

        Assert.Single(await Service.SearchAsync("katariya", includeInactive: false));
        Assert.Single(await Service.SearchAsync("aaofk", includeInactive: false));
        Assert.Single(await Service.SearchAsync("SURAT", includeInactive: false));
        Assert.Equal(2, (await Service.SearchAsync(null, includeInactive: false)).Count);
        Assert.Empty(await Service.SearchAsync("100%", includeInactive: false));
    }

    [Fact]
    public async Task Customer_without_invoices_is_deleted()
    {
        var id = await Service.SaveAsync(Katariya());

        Assert.Equal(CustomerRemovalResult.Deleted, await Service.RemoveAsync(id));
        Assert.Null(await Service.GetAsync(id));
    }

    [Fact]
    public async Task Customer_with_invoices_is_deactivated_and_hidden_from_default_search()
    {
        var id = await Service.SaveAsync(Katariya());
        await AddInvoiceForAsync(id);

        Assert.Equal(CustomerRemovalResult.Deactivated, await Service.RemoveAsync(id));

        Assert.False((await Service.GetAsync(id))!.IsActive);
        Assert.Empty(await Service.SearchAsync(null, includeInactive: false));
        Assert.Single(await Service.SearchAsync(null, includeInactive: true));
    }

    private async Task AddInvoiceForAsync(int customerId)
    {
        var factory = _host.Get<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        db.Invoices.Add(new Invoice
        {
            InvoiceNumber = "T/2026-27/1",
            FinancialYear = "2026-27",
            SequenceNumber = 1,
            InvoiceDate = new DateOnly(2026, 11, 1),
            CustomerId = customerId,
            SellerName = "S", SellerAddress = "A", SellerGstin = "09ACSPH2858M1ZT",
            SellerState = "Uttar Pradesh", SellerStateCode = "09", DeclarationText = "D",
            BuyerName = "B", BuyerAddress = "A", BuyerState = "Maharashtra", BuyerStateCode = "27",
            TaxType = TaxType.InterState, AmountInWords = "W"
        });
        await db.SaveChangesAsync();
    }
}
