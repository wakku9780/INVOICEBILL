using Microsoft.EntityFrameworkCore;
using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Common;
using SareeBilling.Application.CompanyProfiles;
using SareeBilling.Application.Customers;
using SareeBilling.Application.Invoices;
using SareeBilling.Domain.Entities;
using SareeBilling.Domain.Enums;
using SareeBilling.Infrastructure.Persistence;

namespace SareeBilling.Tests.Application;

public sealed class InvoiceServiceTests : IAsyncLifetime
{
    private static readonly DateOnly Nov1 = new(2026, 11, 1);

    private TestHost _host = null!;
    private int _puneCustomerId;
    private int _azamgarhCustomerId;

    private IInvoiceService Invoices => _host.Get<IInvoiceService>();
    private IInvoiceNumberingService Numbering => _host.Get<IInvoiceNumberingService>();

    public async Task InitializeAsync()
    {
        _host = await TestHost.StartAsync();

        await _host.Get<ICompanyProfileService>().SaveAsync(new CompanyProfileDto
        {
            Name = "J A SAREES",
            AddressLine1 = "Purani Basti Lalkunwan",
            City = "Azamgarh",
            Pincode = "276404",
            StateCode = "09",
            Gstin = "09ACSPH2858M1ZT",
            DeclarationText = "Declaration text"
        });

        var customers = _host.Get<ICustomerService>();
        _puneCustomerId = await customers.SaveAsync(new CustomerDto
        {
            Name = "KATARIYA TEXTILES",
            AddressLine1 = "24 Bhavani Peth Palki Chowk",
            City = "Pune",
            Pincode = "411002",
            StateCode = "27",
            Gstin = "27AAOFK4239P1ZK"
        });
        _azamgarhCustomerId = await customers.SaveAsync(new CustomerDto
        {
            Name = "Local Buyer",
            AddressLine1 = "Main Road",
            City = "Mubarakpur",
            StateCode = "09"
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static InvoiceItemInput Saree(decimal quantity = 53m, decimal rate = 6300m, decimal gst = 5m) => new()
    {
        Description = "Pure Lachcha Bdr Booti Tisu Golden Zari",
        HsnCode = "5007",
        Quantity = quantity,
        Unit = "Qty",
        Rate = rate,
        GstRate = gst
    };

    private CreateInvoiceRequest Request(int? customerId = null, DateOnly? date = null, params InvoiceItemInput[] items) => new()
    {
        CustomerId = customerId ?? _puneCustomerId,
        InvoiceDate = date ?? Nov1,
        Destination = "  Pune ",
        Items = items.Length == 0 ? [Saree()] : items
    };

    private async Task<Invoice> LoadAsync(int id) =>
        (await _host.Get<IInvoiceRepository>().GetByIdAsync(id, includeItems: true))!;

    [Fact]
    public async Task Reference_invoice_is_saved_with_number_snapshot_and_igst()
    {
        var created = await Invoices.CreateAsync(Request());

        Assert.Equal("1", created.InvoiceNumber);
        Assert.Equal(350595m, created.GrandTotal);

        var saved = await LoadAsync(created.Id);
        Assert.Equal(TaxType.InterState, saved.TaxType);
        Assert.Equal(333900m, saved.TaxableTotal);
        Assert.Equal(16695m, saved.IgstTotal);
        Assert.Equal(0m, saved.CgstTotal);
        Assert.Equal("Rs. Three Lakh Fifty Thousand Five Hundred Ninety Five Only", saved.AmountInWords);
        Assert.Equal("J A SAREES", saved.SellerName);
        Assert.Equal("09ACSPH2858M1ZT", saved.SellerGstin);
        Assert.Equal("KATARIYA TEXTILES", saved.BuyerName);
        Assert.Equal("Maharashtra", saved.BuyerState);
        Assert.Contains("Pune - 411002", saved.BuyerAddress);
        Assert.Equal("Pune", saved.Destination);

        var item = Assert.Single(saved.Items);
        Assert.Equal(1, item.LineNumber);
        Assert.Equal(5m, item.IgstRate);
        Assert.Equal(16695m, item.IgstAmount);
        Assert.Equal(350595m, item.LineTotal);
    }

    [Fact]
    public async Task Same_state_customer_gets_cgst_and_sgst()
    {
        var created = await Invoices.CreateAsync(Request(customerId: _azamgarhCustomerId, items: Saree(3m, 1234.50m)));

        var saved = await LoadAsync(created.Id);
        Assert.Equal(TaxType.IntraState, saved.TaxType);
        Assert.Equal(92.59m, saved.CgstTotal);
        Assert.Equal(92.59m, saved.SgstTotal);
        Assert.Equal(0m, saved.IgstTotal);
        Assert.Equal(0.32m, saved.RoundOff);
        Assert.Equal(3889m, saved.GrandTotal);
        Assert.Equal(2.5m, saved.Items.Single().CgstRate);
    }

    [Fact]
    public async Task Numbers_are_plain_sequential_and_preview_shows_next()
    {
        Assert.Equal("1", await Invoices.PreviewNextNumberAsync());

        var first = await Invoices.CreateAsync(Request());
        var second = await Invoices.CreateAsync(Request());

        Assert.Equal("1", first.InvoiceNumber);
        Assert.Equal("2", second.InvoiceNumber);
        Assert.Equal("3", await Invoices.PreviewNextNumberAsync());
    }

    [Fact]
    public async Task Numbering_never_restarts_across_financial_years()
    {
        var november = await Invoices.CreateAsync(Request(date: new DateOnly(2026, 11, 1)));
        var previousYear = await Invoices.CreateAsync(Request(date: new DateOnly(2026, 3, 31)));
        var april = await Invoices.CreateAsync(Request(date: new DateOnly(2026, 4, 1)));

        Assert.Equal(["1", "2", "3"], new[] { november.InvoiceNumber, previousYear.InvoiceNumber, april.InvoiceNumber });
        Assert.Equal("2025-26", (await LoadAsync(previousYear.Id)).FinancialYear);
    }

    [Fact]
    public async Task Starting_number_continues_from_tally_and_can_never_go_back()
    {
        await Numbering.SetNextNumberAsync(162);
        Assert.Equal(162, await Numbering.GetNextNumberAsync());

        var created = await Invoices.CreateAsync(Request());
        Assert.Equal("162", created.InvoiceNumber);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Numbering.SetNextNumberAsync(162));
        Assert.Contains("163 or higher", ex.Errors[0]);

        await Numbering.SetNextNumberAsync(170);
        Assert.Equal("170", (await Invoices.CreateAsync(Request())).InvoiceNumber);
    }
    [Fact]
    public async Task Invalid_request_reports_every_problem_and_saves_nothing()
    {
        var badItem = Saree(quantity: 0m, rate: -5m) with { HsnCode = "50", Description = " " };
        var request = Request(customerId: 0, date: DateOnly.FromDateTime(DateTime.Today.AddDays(1)), items: badItem);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Invoices.CreateAsync(request));

        Assert.Contains("Please select a customer.", ex.Errors);
        Assert.Contains("Invoice date cannot be in the future.", ex.Errors);
        Assert.Contains("Item 1: Description is required.", ex.Errors);
        Assert.Contains("Item 1: HSN code must be 4, 6 or 8 digits (for example 5007).", ex.Errors);
        Assert.Contains("Item 1: Quantity must be more than 0.", ex.Errors);
        Assert.Contains("Item 1: Rate must be more than 0.", ex.Errors);
        Assert.Equal(1, await Numbering.GetNextNumberAsync());
    }

    [Fact]
    public async Task Invoice_without_items_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            Invoices.CreateAsync(Request() with { Items = [] }));

        Assert.Contains("Please add at least one item.", ex.Errors);
    }

    [Fact]
    public async Task Inactive_customer_cannot_be_invoiced()
    {
        var customer = (await _host.Get<ICustomerService>().GetAsync(_puneCustomerId))!;
        await _host.Get<ICustomerService>().SaveAsync(customer with { IsActive = false });

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Invoices.CreateAsync(Request()));
        Assert.Contains(ex.Errors, e => e.Contains("inactive"));
    }

    [Fact]
    public async Task Editing_customer_later_does_not_change_saved_invoice()
    {
        var created = await Invoices.CreateAsync(Request());

        var customers = _host.Get<ICustomerService>();
        var customer = (await customers.GetAsync(_puneCustomerId))!;
        await customers.SaveAsync(customer with { Name = "RENAMED TEXTILES", City = "Mumbai" });

        var saved = await LoadAsync(created.Id);
        Assert.Equal("KATARIYA TEXTILES", saved.BuyerName);
        Assert.Contains("Pune", saved.BuyerAddress);
    }

    [Fact]
    public async Task Cancelled_invoice_keeps_its_number_and_needs_a_reason()
    {
        var created = await Invoices.CreateAsync(Request());

        await Assert.ThrowsAsync<ValidationException>(() => Invoices.CancelAsync(created.Id, "  "));

        await Invoices.CancelAsync(created.Id, "Wrong rate entered");
        var saved = await LoadAsync(created.Id);
        Assert.Equal(InvoiceStatus.Cancelled, saved.Status);
        Assert.Equal("Wrong rate entered", saved.CancellationReason);
        Assert.NotNull(saved.CancelledAtUtc);

        await Assert.ThrowsAsync<ValidationException>(() => Invoices.CancelAsync(created.Id, "again"));

        var next = await Invoices.CreateAsync(Request());
        Assert.Equal("2", next.InvoiceNumber);
    }

    [Fact]
    public async Task Failed_save_rolls_back_and_does_not_use_up_a_number()
    {
        // Bypass the service to force the database to reject an item halfway through the save.
        var repository = _host.Get<IInvoiceRepository>();
        var broken = new Invoice
        {
            FinancialYear = "2026-27", InvoiceDate = Nov1, CustomerId = _puneCustomerId,
            SellerName = "S", SellerAddress = "A", SellerGstin = "09ACSPH2858M1ZT", SellerState = "Uttar Pradesh",
            SellerStateCode = "09", DeclarationText = "D", BuyerName = "B", BuyerAddress = "A",
            BuyerState = "Maharashtra", BuyerStateCode = "27", TaxType = TaxType.InterState, AmountInWords = "W"
        };
        broken.Items.Add(new InvoiceItem
        {
            LineNumber = 1, Description = "Bad", HsnCode = "5007", Unit = "Qty", Quantity = 0m, Rate = 1m
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => repository.AddWithNextNumberAsync(broken));

        Assert.Equal(1, await Numbering.GetNextNumberAsync());
        await using var db = await _host.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(0, await db.Invoices.CountAsync());
        Assert.Equal(0, await db.InvoiceItems.CountAsync());
    }
}
