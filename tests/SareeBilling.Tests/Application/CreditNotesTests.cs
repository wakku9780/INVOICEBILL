using SareeBilling.Application.Common;
using SareeBilling.Application.CompanyProfiles;
using SareeBilling.Application.CreditNotes;
using SareeBilling.Application.Customers;
using SareeBilling.Application.Dashboard;
using SareeBilling.Application.Invoices;
using SareeBilling.Application.Receivables;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Tests.Application;

/// <summary>Credit notes (sales returns and value reductions) on a real SQLite database.</summary>
public sealed class CreditNotesTests : IAsyncLifetime
{
    private static readonly DateOnly InvoiceDay = new(2026, 10, 1);
    private static readonly DateOnly ReturnDay = new(2026, 10, 20);

    private TestHost _host = null!;
    private int _puneId;   // other state: IGST
    private int _localId;  // same state as the seller: CGST + SGST

    private IInvoiceService Invoices => _host.Get<IInvoiceService>();
    private ICreditNoteService CreditNotes => _host.Get<ICreditNoteService>();
    private IReceiptService Receipts => _host.Get<IReceiptService>();

    public async Task InitializeAsync()
    {
        _host = await TestHost.StartAsync();

        await _host.Get<ICompanyProfileService>().SaveAsync(new CompanyProfileDto
        {
            Name = "TEST SAREES", AddressLine1 = "Main Road", City = "Azamgarh", Pincode = "276404",
            StateCode = "09", Gstin = "09ACSPH2858M1ZT", DeclarationText = "Declaration text"
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

    /// <summary>Silk sarees at 1,000 + 5% GST, plus optional cotton sarees at 400 + 5%.</summary>
    private async Task<CreditableInvoice> SellAsync(int customerId, decimal silk, decimal cotton = 0)
    {
        List<InvoiceItemInput> items = [new() { Description = "Silk saree", HsnCode = "5007", Quantity = silk, Unit = "Pcs", Rate = 1000m, GstRate = 5m }];
        if (cotton > 0)
        {
            items.Add(new() { Description = "Cotton saree", HsnCode = "5208", Quantity = cotton, Unit = "Pcs", Rate = 400m, GstRate = 5m });
        }

        var created = await Invoices.CreateAsync(new CreateInvoiceRequest { CustomerId = customerId, InvoiceDate = InvoiceDay, Items = items });
        return (await CreditNotes.GetInvoiceAsync(created.Id))!;
    }

    private Task<CreatedCreditNote> ReturnAsync(CreditableInvoice invoice, params (int Line, decimal Quantity)[] lines) =>
        CreditNotes.CreateAsync(new CreateCreditNoteRequest
        {
            InvoiceId = invoice.InvoiceId,
            CreditNoteDate = ReturnDay,
            Kind = CreditNoteKind.GoodsReturn,
            Reason = "Damaged pieces returned",
            Lines = lines.Select(l => new CreditNoteLineRequest(invoice.Lines[l.Line - 1].InvoiceItemId, l.Quantity, 0m)).ToList()
        });

    private Task<CreatedCreditNote> ReduceAsync(CreditableInvoice invoice, int line, decimal amount) =>
        CreditNotes.CreateAsync(new CreateCreditNoteRequest
        {
            InvoiceId = invoice.InvoiceId,
            CreditNoteDate = ReturnDay,
            Kind = CreditNoteKind.AmountOnly,
            Reason = "Rate difference",
            Lines = [new CreditNoteLineRequest(invoice.Lines[line - 1].InvoiceItemId, 0m, amount)]
        });

    private async Task<CustomerAccount> AccountAsync(int customerId) => (await Receipts.GetAccountAsync(customerId))!;

    private static AccountBill InvoiceBill(CustomerAccount account, CreditableInvoice invoice) =>
        account.Bills.Single(b => b.Ref == new BillRef(BillKind.Invoice, invoice.InvoiceId));

    [Fact]
    public async Task Returned_goods_use_the_invoice_GST_and_reduce_what_is_pending()
    {
        var invoice = await SellAsync(_localId, 10);   // 10,000 + CGST 250 + SGST 250 = 10,500

        var note = await ReturnAsync(invoice, (1, 3));

        Assert.Equal("CN-1", note.CreditNoteNumber);
        Assert.Equal(3150m, note.GrandTotal);
        Assert.Equal(3150m, note.AppliedToInvoice);
        Assert.Equal(0m, note.KeptAsAdvance);

        var saved = (await CreditNotes.SearchAsync(new CreditNoteSearchCriteria())).Single();
        Assert.Equal((3000m, 150m), (saved.TaxableTotal, saved.TotalTax));

        var account = await AccountAsync(_localId);
        Assert.Equal(7350m, InvoiceBill(account, invoice).Pending);
        Assert.Equal(7350m, account.Balance);
        Assert.Equal(0m, account.Advance);
    }

    [Fact]
    public async Task Cannot_return_more_than_billed_less_earlier_returns()
    {
        var invoice = await SellAsync(_puneId, 10);
        await ReturnAsync(invoice, (1, 3));

        var tooMany = await Assert.ThrowsAsync<ValidationException>(() => ReturnAsync(invoice, (1, 8)));
        Assert.Contains("only 7 Pcs can still be returned", tooMany.Errors.Single());

        var rest = await ReturnAsync(invoice, (1, 7));
        Assert.Equal(7350m, rest.GrandTotal);   // 7,000 + IGST 350

        var nothingLeft = await Assert.ThrowsAsync<ValidationException>(() => ReturnAsync(invoice, (1, 1)));
        Assert.Contains("already been returned", nothingLeft.Errors.Single());

        Assert.Equal(0m, (await AccountAsync(_puneId)).Balance);
    }

    [Fact]
    public async Task Returning_everything_left_credits_exactly_what_is_left_to_the_paisa()
    {
        // 3 x 333.33 = 999.99 taxable; returning 1 + 1 + 1 must add up to exactly 999.99.
        var created = await Invoices.CreateAsync(new CreateInvoiceRequest
        {
            CustomerId = _puneId, InvoiceDate = InvoiceDay,
            Items = [new() { Description = "Saree", HsnCode = "5007", Quantity = 3, Unit = "Pcs", Rate = 333.33m, GstRate = 5m }]
        });
        var invoice = (await CreditNotes.GetInvoiceAsync(created.Id))!;

        await ReturnAsync(invoice, (1, 1));
        await ReturnAsync(invoice, (1, 1));
        await ReturnAsync(invoice, (1, 1));

        var after = (await CreditNotes.GetInvoiceAsync(created.Id))!;
        Assert.Equal(0m, after.Lines.Single().RemainingQuantity);
        Assert.Equal(0m, after.Lines.Single().RemainingTaxable);
    }

    [Fact]
    public async Task A_credit_note_on_a_paid_invoice_becomes_advance_and_on_a_part_paid_one_is_split()
    {
        var paid = await SellAsync(_puneId, 1);              // 1,050
        var partPaid = await SellAsync(_puneId, 2);          // 2,100
        await Receipts.CreateAsync(new CreateReceiptRequest
        {
            CustomerId = _puneId, ReceiptDate = new DateOnly(2026, 10, 10), Amount = 2550m, Reference = "UTR",
            Allocations =
            [
                new AllocationLine(new BillRef(BillKind.Invoice, paid.InvoiceId), 1050m),
                new AllocationLine(new BillRef(BillKind.Invoice, partPaid.InvoiceId), 1500m)
            ]
        });

        var onPaid = await ReturnAsync(paid, (1, 1));
        Assert.Equal((0m, 1050m), (onPaid.AppliedToInvoice, onPaid.KeptAsAdvance));

        var onPartPaid = await ReturnAsync(partPaid, (1, 1));
        Assert.Equal((600m, 450m), (onPartPaid.AppliedToInvoice, onPartPaid.KeptAsAdvance));

        var account = await AccountAsync(_puneId);
        Assert.Equal(0m, account.Pending);
        Assert.Equal(1500m, account.Advance);
        Assert.Equal(-1500m, account.Balance);
    }

    [Fact]
    public async Task Amount_only_credit_note_adds_GST_and_later_returns_use_the_reduced_value()
    {
        var invoice = await SellAsync(_puneId, 10, cotton: 5);   // silk 10,000, cotton 2,000

        var discount = await ReduceAsync(invoice, 1, 500m);
        Assert.Equal(525m, discount.GrandTotal);                 // 500 + IGST 25

        var tooMuch = await Assert.ThrowsAsync<ValidationException>(() => ReduceAsync(invoice, 1, 9500.01m));
        Assert.Contains("at most 9,500.00", tooMuch.Errors.Single());

        // 2 of the 10 silk sarees come back: valued at 9,500 / 10 = 950 each after the discount.
        invoice = (await CreditNotes.GetInvoiceAsync(invoice.InvoiceId))!;
        var (calc, errors) = CreditNoteRules.Calculate(invoice, CreditNoteKind.GoodsReturn,
            [new CreditNoteLineRequest(invoice.Lines[0].InvoiceItemId, 2m, 0m)]);
        Assert.Empty(errors);
        Assert.Equal((1900m, 950m, 95m), (calc!.TaxableTotal, calc.Lines.Single().Rate, calc.IgstTotal));
        Assert.Equal(0m, calc.CgstTotal);
    }

    [Fact]
    public async Task Cancelling_a_credit_note_makes_the_invoice_and_bills_paid_from_its_advance_pending_again()
    {
        var first = await SellAsync(_localId, 1);    // 1,050, paid in full
        var later = await SellAsync(_localId, 2);    // 2,100, unpaid
        await Receipts.CreateAsync(new CreateReceiptRequest
        {
            CustomerId = _localId, ReceiptDate = new DateOnly(2026, 10, 10), Amount = 1050m, Reference = "UTR",
            Allocations = [new AllocationLine(new BillRef(BillKind.Invoice, first.InvoiceId), 1050m)]
        });

        var note = await ReturnAsync(first, (1, 1));           // all 1,050 becomes advance
        await Receipts.AdjustAdvanceAsync(_localId, [new AllocationLine(new BillRef(BillKind.Invoice, later.InvoiceId), 1050m)]);
        Assert.Equal(1050m, InvoiceBill(await AccountAsync(_localId), later).Pending);

        await CreditNotes.CancelAsync(note.Id, "Entered by mistake");

        var account = await AccountAsync(_localId);
        Assert.Equal(2100m, InvoiceBill(account, later).Pending);
        Assert.Equal(2100m, account.Balance);
        Assert.Equal(1, (await CreditNotes.GetInvoiceAsync(first.InvoiceId))!.Lines.Single().RemainingQuantity);

        await Assert.ThrowsAsync<ValidationException>(() => CreditNotes.CancelAsync(note.Id, "Again"));
        await Assert.ThrowsAsync<ValidationException>(() => CreditNotes.CancelAsync(note.Id, " "));
    }

    [Fact]
    public async Task An_invoice_with_a_credit_note_cannot_be_cancelled_until_the_credit_note_is()
    {
        var invoice = await SellAsync(_puneId, 2);
        var note = await ReturnAsync(invoice, (1, 1));

        var blocked = await Assert.ThrowsAsync<ValidationException>(() => Invoices.CancelAsync(invoice.InvoiceId, "Wrong customer"));
        Assert.Equal($"Invoice {invoice.InvoiceNumber} has credit note CN-1. Cancel the credit note first, then the invoice.", blocked.Errors.Single());
        Assert.Equal(["CN-1"], await CreditNotes.GetActiveNumbersForInvoiceAsync(invoice.InvoiceId));

        await CreditNotes.CancelAsync(note.Id, "Wrong invoice");
        await Invoices.CancelAsync(invoice.InvoiceId, "Wrong customer");

        var noMore = await Assert.ThrowsAsync<ValidationException>(() => ReturnAsync(invoice, (1, 1)));
        Assert.Contains("is cancelled", noMore.Errors.Single());
    }

    [Fact]
    public async Task Reason_date_and_at_least_one_line_are_required()
    {
        var invoice = await SellAsync(_puneId, 2);

        var noReason = await Assert.ThrowsAsync<ValidationException>(() => CreditNotes.CreateAsync(new CreateCreditNoteRequest
        {
            InvoiceId = invoice.InvoiceId, CreditNoteDate = ReturnDay, Reason = "  ",
            Lines = [new CreditNoteLineRequest(invoice.Lines[0].InvoiceItemId, 1m, 0m)]
        }));
        Assert.Contains(noReason.Errors, e => e.Contains("Reason"));

        var beforeInvoice = await Assert.ThrowsAsync<ValidationException>(() => CreditNotes.CreateAsync(new CreateCreditNoteRequest
        {
            InvoiceId = invoice.InvoiceId, CreditNoteDate = InvoiceDay.AddDays(-1), Reason = "Return",
            Lines = [new CreditNoteLineRequest(invoice.Lines[0].InvoiceItemId, 1m, 0m)]
        }));
        Assert.Contains(beforeInvoice.Errors, e => e.Contains("before the invoice date"));

        var nothing = await Assert.ThrowsAsync<ValidationException>(() => ReturnAsync(invoice, (1, 0)));
        Assert.Contains("at least one item", nothing.Errors.Single());

        var tooPrecise = await Assert.ThrowsAsync<ValidationException>(() => ReturnAsync(invoice, (1, 0.0005m)));
        Assert.Contains("3 decimal places", tooPrecise.Errors.Single());

        Assert.Empty(await CreditNotes.SearchAsync(new CreditNoteSearchCriteria()));
    }

    [Theory]
    [InlineData("2026-11-01", "2027-11-30")]
    [InlineData("2027-03-31", "2027-11-30")]
    [InlineData("2027-04-01", "2028-11-30")]
    public void GST_on_an_invoice_can_be_reduced_until_30_November_after_its_financial_year(string invoiceDate, string deadline)
    {
        Assert.Equal(DateOnly.Parse(deadline), CreditNoteRules.GstDeadline(DateOnly.Parse(invoiceDate)));
        Assert.False(CreditNoteRules.IsPastGstDeadline(DateOnly.Parse(invoiceDate), DateOnly.Parse(deadline)));
        Assert.True(CreditNoteRules.IsPastGstDeadline(DateOnly.Parse(invoiceDate), DateOnly.Parse(deadline).AddDays(1)));
    }

    [Fact]
    public async Task Ledger_dashboard_invoice_history_and_print_show_the_credit_note()
    {
        var invoice = await SellAsync(_localId, 10);
        var note = await ReturnAsync(invoice, (1, 2));   // 2,100 (GST 100)

        var ledger = await _host.Get<ICustomerLedgerService>().GetAsync(_localId, new DateOnly(2026, 4, 1), new DateOnly(2026, 10, 31));
        var entry = ledger.Entries.Single(e => e.Type == LedgerEntryType.CreditNote);
        Assert.Equal(("CN-1", 2100m), (entry.Reference, entry.Credit));
        Assert.Contains($"Goods returned on invoice {invoice.InvoiceNumber}", entry.Particulars);
        Assert.Equal(8400m, ledger.ClosingBalance);
        Assert.Contains("Credit note,CN-1", LedgerFormat.ToCsv(ledger));

        var summary = await _host.Get<IDashboardService>().GetSummaryAsync(new DateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));
        Assert.Equal((10500m, 2100m, 8400m), (summary.Sales, summary.Returns, summary.NetSales));
        Assert.Equal((500m, 100m, 400m), (summary.GstCollected, summary.ReturnsTax, summary.NetGst));
        Assert.Equal(1, summary.CreditNoteCount);

        var history = await Invoices.SearchAsync(new InvoiceSearchCriteria());
        Assert.Equal(2100m, history.Single().Credited);

        var printout = await _host.Get<IAccountsPrintService>().CreditNoteAsync(note.Id);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(printout.Pdf, 0, 4));
        Assert.NotEmpty(printout.PageImages);
        Assert.Equal("CreditNote-CN-1.pdf", printout.SuggestedFileName);
        KeepForVisualCheck(printout.Pdf, printout.PageImages[0], "credit-note");

        var discount = await ReduceAsync((await CreditNotes.GetInvoiceAsync(invoice.InvoiceId))!, 1, 300m);
        var amountOnly = await _host.Get<IAccountsPrintService>().CreditNoteAsync(discount.Id);
        KeepForVisualCheck(amountOnly.Pdf, amountOnly.PageImages[0], "credit-note-amount");
    }

    /// <summary>Set SAREEBILLING_PDF_OUT to a folder to keep the PDFs and page images for a visual check.</summary>
    private static void KeepForVisualCheck(byte[] pdf, byte[] image, string name)
    {
        var folder = Environment.GetEnvironmentVariable("SAREEBILLING_PDF_OUT");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), pdf);
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), image);
    }
}
