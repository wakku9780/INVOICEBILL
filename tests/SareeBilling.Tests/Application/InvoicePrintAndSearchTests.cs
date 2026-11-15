using System.Text;
using SareeBilling.Application.CompanyProfiles;
using SareeBilling.Application.Customers;
using SareeBilling.Application.Invoices;
using SareeBilling.Domain.Entities;
using SareeBilling.Domain.Enums;
using SareeBilling.Domain.Gst;

namespace SareeBilling.Tests.Application;

/// <summary>
/// Renders real invoices through QuestPDF. Set SAREEBILLING_PDF_OUT to a folder to keep the
/// generated PDFs and page images for a visual check.
/// </summary>
public sealed class InvoicePrintAndSearchTests : IAsyncLifetime
{
    private TestHost _host = null!;
    private int _puneId;
    private int _localId;

    private IInvoiceService Invoices => _host.Get<IInvoiceService>();
    private IInvoicePrintService Printing => _host.Get<IInvoicePrintService>();

    public async Task InitializeAsync()
    {
        _host = await TestHost.StartAsync();

        await _host.Get<ICompanyProfileService>().SaveAsync(new CompanyProfileDto
        {
            Name = "J A SAREES",
            AddressLine1 = "Purani Basti Lalkunwan",
            AddressLine2 = "Mubarakpur",
            City = "Azamgarh",
            Pincode = "276404",
            StateCode = "09",
            Gstin = "09ACSPH2858M1ZT",
            Phone = "9000000003",
            InvoicePrefix = "JAS",
            BankName = "State Bank of India",
            BankAccountNumber = "00000000000",
            BankIfsc = "SBIN0001234",
            BankBranch = "Mubarakpur",
            DeclarationText = "We declare that this invoice shows the actual price of the goods described and that all particulars are true and correct."
        });

        var customers = _host.Get<ICustomerService>();
        _puneId = await customers.SaveAsync(new CustomerDto
        {
            Name = "KATARIYA TEXTILES",
            AddressLine1 = "24 Bhavani Peth Palki Chowk",
            AddressLine2 = "Opp. Canara Bank",
            City = "Pune",
            Pincode = "411002",
            StateCode = "27",
            Gstin = "27AAOFK4239P1ZK",
            Phone = "9000000001"
        });
        _localId = await customers.SaveAsync(new CustomerDto
        {
            Name = "Rani Saree Centre",
            AddressLine1 = "Chowk Bazar",
            City = "Varanasi",
            StateCode = "09"
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static InvoiceItemInput Item(string description, decimal qty, decimal rate, decimal gst = 5m, string hsn = "5007") => new()
    {
        Description = description, HsnCode = hsn, Quantity = qty, Unit = "Qty", Rate = rate, GstRate = gst
    };

    private Task<CreatedInvoice> CreateAsync(int customerId, DateOnly date, params InvoiceItemInput[] items) =>
        Invoices.CreateAsync(new CreateInvoiceRequest
        {
            CustomerId = customerId,
            InvoiceDate = date,
            PaymentTerms = "30 days",
            Destination = "Pune",
            DispatchedThrough = "VRL Logistics",
            Items = items
        });

    private static void KeepForVisualCheck(InvoicePrintout printout, string name)
    {
        var folder = Environment.GetEnvironmentVariable("SAREEBILLING_PDF_OUT");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), printout.Pdf);
        for (var i = 0; i < printout.PageImages.Count; i++)
        {
            File.WriteAllBytes(Path.Combine(folder, $"{name}-page{i + 1}.png"), printout.PageImages[i]);
        }
    }

    [Fact]
    public async Task Reference_invoice_renders_one_page_pdf_and_image()
    {
        var created = await CreateAsync(_puneId, new DateOnly(2026, 11, 1),
            Item("Pure Lachcha Bdr Booti Tisu Golden Zari", 53m, 6300m));

        var printout = await Printing.CreateAsync(created.Id);
        KeepForVisualCheck(printout, "reference-igst");

        Assert.Equal("%PDF", Encoding.ASCII.GetString(printout.Pdf, 0, 4));
        Assert.Single(printout.PageImages);
        Assert.Equal("JAS-2026-27-1.pdf", printout.SuggestedFileName);
        Assert.Equal("KATARIYA TEXTILES", printout.BuyerName);
        Assert.False(printout.IsCancelled);
    }

    [Fact]
    public async Task Same_state_invoice_with_mixed_rates_renders()
    {
        var created = await CreateAsync(_localId, new DateOnly(2026, 11, 2),
            Item("Banarasi Silk Saree", 3m, 1234.50m),
            Item("Cotton Saree", 2.5m, 333.33m, gst: 5m, hsn: "5208"),
            Item("Saree Fall and Blouse Stitching", 4m, 150m, gst: 18m, hsn: "998821"));

        var printout = await Printing.CreateAsync(created.Id);
        KeepForVisualCheck(printout, "intra-state-mixed");

        Assert.NotEmpty(printout.Pdf);
        Assert.Single(printout.PageImages);
    }

    [Fact]
    public async Task Long_invoice_flows_onto_more_pages()
    {
        var items = Enumerable.Range(1, 60)
            .Select(i => Item($"Saree design {i:000} with zari border", 1m + i % 4, 1500m + i))
            .ToArray();
        var created = await CreateAsync(_puneId, new DateOnly(2026, 11, 3), items);

        var printout = await Printing.CreateAsync(created.Id);
        KeepForVisualCheck(printout, "long-invoice");

        Assert.True(printout.PageImages.Count >= 2, $"Expected at least 2 pages, got {printout.PageImages.Count}.");
    }

    [Fact]
    public async Task Cancelled_invoice_is_marked()
    {
        var created = await CreateAsync(_puneId, new DateOnly(2026, 11, 4), Item("Saree", 1m, 1000m));
        await Invoices.CancelAsync(created.Id, "Wrong rate entered");

        var printout = await Printing.CreateAsync(created.Id);
        KeepForVisualCheck(printout, "cancelled");

        Assert.True(printout.IsCancelled);
        Assert.Single(printout.PageImages);
    }

    [Fact]
    public async Task Search_filters_by_text_dates_and_status_newest_first()
    {
        var first = await CreateAsync(_puneId, new DateOnly(2026, 10, 5), Item("Saree", 1m, 1000m));
        var second = await CreateAsync(_localId, new DateOnly(2026, 11, 10), Item("Saree", 2m, 1000m));
        var third = await CreateAsync(_puneId, new DateOnly(2026, 11, 12), Item("Saree", 3m, 1000m));
        await Invoices.CancelAsync(third.Id, "Duplicate");

        var all = await Invoices.SearchAsync(new InvoiceSearchCriteria());
        Assert.Equal([third.Id, second.Id, first.Id], all.Select(i => i.Id));
        Assert.Equal(InvoiceStatus.Cancelled, all[0].Status);

        Assert.Equal(2, (await Invoices.SearchAsync(new InvoiceSearchCriteria { Text = "katariya" })).Count);
        Assert.Single(await Invoices.SearchAsync(new InvoiceSearchCriteria { Text = "/2026-27/2" }));
        Assert.Single(await Invoices.SearchAsync(new InvoiceSearchCriteria { Text = "AAOFK", IncludeCancelled = false }));

        var november = await Invoices.SearchAsync(new InvoiceSearchCriteria
        {
            FromDate = new DateOnly(2026, 11, 1),
            ToDate = new DateOnly(2026, 11, 30)
        });
        Assert.Equal([third.Id, second.Id], november.Select(i => i.Id));

        var summary = all.Single(i => i.Id == second.Id);
        Assert.Equal(2000m, summary.TaxableTotal);
        Assert.Equal(100m, summary.TotalTax);
        Assert.Equal(2100m, summary.GrandTotal);
    }

    [Theory]
    [InlineData("JAS/2026-27/161", "JAS-2026-27-161.pdf")]
    [InlineData("A-B/2025-26/7", "A-B-2025-26-7.pdf")]
    public void File_name_has_no_slashes(string number, string expected)
    {
        Assert.Equal(expected, InvoicePrintService.SuggestFileName(number));
    }

    [Fact]
    public void Hsn_summary_groups_by_hsn_and_rate()
    {
        InvoiceItem Line(string hsn, decimal rate, decimal taxable, decimal igst) =>
            new() { HsnCode = hsn, GstRate = rate, IgstRate = rate, TaxableAmount = taxable, IgstAmount = igst };

        var summary = HsnTaxSummary.From(
        [
            Line("5007", 5m, 1000m, 50m),
            Line("5007", 5m, 500m, 25m),
            Line("5007", 18m, 100m, 18m),
            Line("5208", 5m, 200m, 10m)
        ]);

        Assert.Equal(3, summary.Count);
        Assert.Equal(1500m, summary[0].TaxableAmount);
        Assert.Equal(75m, summary[0].TotalTax);
        Assert.Equal(18m, summary[1].GstRate);
        Assert.Equal("5208", summary[2].HsnCode);
    }
}
