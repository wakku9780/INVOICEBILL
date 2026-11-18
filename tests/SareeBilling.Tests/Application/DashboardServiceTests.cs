using SareeBilling.Application.Common;
using SareeBilling.Application.CompanyProfiles;
using SareeBilling.Application.Customers;
using SareeBilling.Application.Dashboard;
using SareeBilling.Application.Invoices;

namespace SareeBilling.Tests.Application;

public sealed class DashboardServiceTests : IAsyncLifetime
{
    private TestHost _host = null!;
    private int _puneId;
    private int _localId;

    private IInvoiceService Invoices => _host.Get<IInvoiceService>();
    private IDashboardService Dashboard => _host.Get<IDashboardService>();

    public async Task InitializeAsync()
    {
        _host = await TestHost.StartAsync();

        await _host.Get<ICompanyProfileService>().SaveAsync(new CompanyProfileDto
        {
            Name = "TEST SAREES",
            AddressLine1 = "Main Road",
            City = "Azamgarh",
            Pincode = "276404",
            StateCode = "09",
            Gstin = "09ACSPH2858M1ZT",
            DeclarationText = "Declaration text"
        });

        var customers = _host.Get<ICustomerService>();
        _puneId = await customers.SaveAsync(new CustomerDto
        {
            Name = "KATARIYA TEXTILES", AddressLine1 = "Bhavani Peth", City = "Pune", StateCode = "27", Gstin = "27AAOFK4239P1ZK"
        });
        _localId = await customers.SaveAsync(new CustomerDto
        {
            Name = "Local Buyer", AddressLine1 = "Main Road", City = "Mubarakpur", StateCode = "09"
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>Taxable = quantity x 1,000; GST 5%.</summary>
    private Task<CreatedInvoice> SellAsync(int customerId, DateOnly date, decimal quantity, decimal rate = 1000m) =>
        Invoices.CreateAsync(new CreateInvoiceRequest
        {
            CustomerId = customerId,
            InvoiceDate = date,
            Items = [new InvoiceItemInput { Description = "Silk saree", HsnCode = "5007", Quantity = quantity, Unit = "Pcs", Rate = rate, GstRate = 5m }]
        });

    [Theory]
    [InlineData(DashboardPeriod.Today, "2026-11-18", "2026-11-18")]
    [InlineData(DashboardPeriod.ThisWeek, "2026-11-16", "2026-11-18")]
    [InlineData(DashboardPeriod.ThisMonth, "2026-11-01", "2026-11-18")]
    [InlineData(DashboardPeriod.ThisFinancialYear, "2026-04-01", "2026-11-18")]
    public void Periods_end_today_and_start_on_monday_month_or_april(DashboardPeriod period, string from, string to)
    {
        var wednesday = new DateOnly(2026, 11, 18);

        var range = DashboardPeriods.Resolve(period, wednesday);

        Assert.Equal(DateOnly.Parse(from), range.From);
        Assert.Equal(DateOnly.Parse(to), range.To);
    }

    [Fact]
    public void Week_starting_on_sunday_goes_back_to_monday_and_financial_year_before_april_uses_last_year()
    {
        var sunday = new DateOnly(2026, 11, 22);
        Assert.Equal(new DateOnly(2026, 11, 16), DashboardPeriods.Resolve(DashboardPeriod.ThisWeek, sunday).From);

        var february = new DateOnly(2027, 2, 10);
        Assert.Equal(new DateOnly(2026, 4, 1), DashboardPeriods.Resolve(DashboardPeriod.ThisFinancialYear, february).From);
    }

    [Fact]
    public void Custom_range_must_have_both_dates_in_order()
    {
        var today = new DateOnly(2026, 11, 18);

        Assert.Throws<ValidationException>(() => DashboardPeriods.Resolve(DashboardPeriod.Custom, today, null, today));
        Assert.Throws<ValidationException>(() =>
            DashboardPeriods.Resolve(DashboardPeriod.Custom, today, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 1)));

        var range = DashboardPeriods.Resolve(DashboardPeriod.Custom, today, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.Equal(31, range.Days);
    }

    [Fact]
    public async Task Summary_adds_up_only_final_invoices_in_the_period()
    {
        await SellAsync(_puneId, new DateOnly(2026, 10, 5), 1);   // 1,050.00 — before the period
        await SellAsync(_puneId, new DateOnly(2026, 11, 2), 2);   // 2,100.00 (IGST)
        await SellAsync(_localId, new DateOnly(2026, 11, 3), 2);  // 2,100.00 (CGST + SGST)
        var cancelled = await SellAsync(_localId, new DateOnly(2026, 11, 4), 5); // 5,250.00, then cancelled
        await Invoices.CancelAsync(cancelled.Id, "Wrong customer");

        var summary = await Dashboard.GetSummaryAsync(new DateRange(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10)));

        Assert.Equal(4200m, summary.Sales);
        Assert.Equal(4000m, summary.TaxableValue);
        Assert.Equal(200m, summary.GstCollected);
        Assert.Equal(2, summary.InvoiceCount);
        Assert.Equal(1, summary.CancelledCount);
        Assert.Equal(2, summary.CustomersBilled);
    }

    [Fact]
    public async Task Top_customers_are_ranked_by_sales_and_recent_invoices_include_cancelled_ones_newest_first()
    {
        await SellAsync(_puneId, new DateOnly(2026, 11, 2), 1);   // 1,050
        await SellAsync(_puneId, new DateOnly(2026, 11, 3), 1);   // 1,050  -> Pune 2,100 over 2 invoices
        await SellAsync(_localId, new DateOnly(2026, 11, 3), 3);  // 3,150  -> Local 3,150
        var cancelled = await SellAsync(_puneId, new DateOnly(2026, 11, 4), 10);
        await Invoices.CancelAsync(cancelled.Id, "Duplicate");

        var summary = await Dashboard.GetSummaryAsync(new DateRange(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10)));

        Assert.Collection(summary.TopCustomers,
            c => { Assert.Equal("Local Buyer", c.Name); Assert.Equal(3150m, c.Sales); Assert.Equal(1, c.InvoiceCount); },
            c => { Assert.Equal("KATARIYA TEXTILES", c.Name); Assert.Equal(2100m, c.Sales); Assert.Equal(2, c.InvoiceCount); });

        Assert.Equal(4, summary.RecentInvoices.Count);
        Assert.True(summary.RecentInvoices[0].IsCancelled);
        Assert.Equal(cancelled.InvoiceNumber, summary.RecentInvoices[0].InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 11, 3), summary.RecentInvoices[1].InvoiceDate);
        Assert.Equal(new DateOnly(2026, 11, 2), summary.RecentInvoices[3].InvoiceDate);
    }

    [Fact]
    public async Task Trend_covers_six_months_ending_in_the_period_month_even_when_the_period_is_one_day()
    {
        await SellAsync(_puneId, new DateOnly(2026, 5, 31), 4);   // May: outside the six months
        await SellAsync(_puneId, new DateOnly(2026, 6, 1), 1);    // June 1,050
        await SellAsync(_localId, new DateOnly(2026, 10, 15), 2); // October 2,100
        await SellAsync(_localId, new DateOnly(2026, 11, 1), 1);  // November 1,050
        var cancelled = await SellAsync(_localId, new DateOnly(2026, 11, 2), 9);
        await Invoices.CancelAsync(cancelled.Id, "Mistake");

        var day = new DateOnly(2026, 11, 2);
        var summary = await Dashboard.GetSummaryAsync(new DateRange(day, day));

        Assert.Equal(
            [new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1),
             new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1)],
            summary.Trend.Select(m => m.Month));
        Assert.Equal([1050m, 0m, 0m, 0m, 2100m, 1050m], summary.Trend.Select(m => m.Sales));
        Assert.Equal(0m, summary.Sales);
        Assert.Equal(1, summary.CancelledCount);
    }

    [Fact]
    public async Task Empty_database_gives_zeros_not_errors()
    {
        var summary = await Dashboard.GetSummaryAsync(new DateRange(new DateOnly(2026, 4, 1), new DateOnly(2026, 11, 18)));

        Assert.Equal(0m, summary.Sales);
        Assert.Equal(0, summary.InvoiceCount);
        Assert.Empty(summary.TopCustomers);
        Assert.Empty(summary.RecentInvoices);
        Assert.Equal(6, summary.Trend.Count);
        Assert.All(summary.Trend, m => Assert.Equal(0m, m.Sales));
    }
}
