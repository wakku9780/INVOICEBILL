using SareeBilling.Application.Common;
using SareeBilling.Application.CompanyProfiles;
using SareeBilling.Application.Customers;
using SareeBilling.Application.Dashboard;
using SareeBilling.Application.Invoices;
using SareeBilling.Application.Receivables;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Tests.Application;

/// <summary>Receipts, bill-wise settlement, advances, ledger and outstanding report on a real SQLite database.</summary>
public sealed class ReceivablesTests : IAsyncLifetime
{
    private TestHost _host = null!;
    private int _puneId;
    private int _localId;

    private IInvoiceService Invoices => _host.Get<IInvoiceService>();
    private IReceiptService Receipts => _host.Get<IReceiptService>();
    private IOpeningBillService OpeningBills => _host.Get<IOpeningBillService>();

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
            Name = "Local Buyer", AddressLine1 = "Main Road", City = "Mubarakpur", StateCode = "09", CreditDays = 15
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>1,050.00 per piece (1,000 + 5% GST).</summary>
    private async Task<BillRef> SellAsync(int customerId, DateOnly date, decimal pieces = 1)
    {
        var created = await Invoices.CreateAsync(new CreateInvoiceRequest
        {
            CustomerId = customerId,
            InvoiceDate = date,
            Items = [new InvoiceItemInput { Description = "Silk saree", HsnCode = "5007", Quantity = pieces, Unit = "Pcs", Rate = 1000m, GstRate = 5m }]
        });
        return new BillRef(BillKind.Invoice, created.Id);
    }

    private Task<CreatedReceipt> ReceiveAsync(int customerId, DateOnly date, decimal amount, params AllocationLine[] lines) =>
        Receipts.CreateAsync(new CreateReceiptRequest
        {
            CustomerId = customerId,
            ReceiptDate = date,
            Amount = amount,
            Mode = PaymentMode.BankTransfer,
            Reference = "UTR 1",
            Allocations = lines
        });

    private async Task<CustomerAccount> AccountAsync(int customerId) => (await Receipts.GetAccountAsync(customerId))!;

    private static AccountBill Bill(CustomerAccount account, BillRef bill) => account.Bills.Single(b => b.Ref == bill);

    [Fact]
    public async Task New_invoice_is_due_after_the_customers_credit_days()
    {
        var pune = await SellAsync(_puneId, new DateOnly(2026, 10, 1));
        var local = await SellAsync(_localId, new DateOnly(2026, 10, 1));

        Assert.Equal(new DateOnly(2026, 10, 31), Bill(await AccountAsync(_puneId), pune).DueDate);
        Assert.Equal(new DateOnly(2026, 10, 16), Bill(await AccountAsync(_localId), local).DueDate);
    }

    [Fact]
    public async Task Credit_days_must_be_between_0_and_365()
    {
        var customers = _host.Get<ICustomerService>();
        var dto = (await customers.GetAsync(_puneId))!;

        await Assert.ThrowsAsync<ValidationException>(() => customers.SaveAsync(dto with { CreditDays = 400 }));
        await Assert.ThrowsAsync<ValidationException>(() => customers.SaveAsync(dto with { CreditDays = -1 }));

        await customers.SaveAsync(dto with { CreditDays = 0 });
        Assert.Equal(0, (await customers.GetAsync(_puneId))!.CreditDays);
    }

    [Fact]
    public async Task Auto_fill_pays_oldest_bills_first_and_one_receipt_can_part_pay_several_invoices()
    {
        var older = await SellAsync(_puneId, new DateOnly(2026, 10, 1));     // 1,050
        var newer = await SellAsync(_puneId, new DateOnly(2026, 10, 10), 2); // 2,100

        var suggestion = AllocationRules.AutoFill(await AccountAsync(_puneId), 2000m);
        Assert.Equal([new AllocationLine(older, 1050m), new AllocationLine(newer, 950m)], suggestion);

        var receipt = await ReceiveAsync(_puneId, new DateOnly(2026, 11, 1), 2000m, [.. suggestion]);
        Assert.Equal("R-1", receipt.ReceiptNumber);
        Assert.Equal(0m, receipt.KeptAsAdvance);

        var account = await AccountAsync(_puneId);
        Assert.Equal(0m, Bill(account, older).Pending);
        Assert.Equal(1150m, Bill(account, newer).Pending);
        Assert.Equal(1150m, account.Balance);
        Assert.Equal(0m, account.Advance);

        // A second receipt settles the rest of the same invoice.
        var second = await ReceiveAsync(_puneId, new DateOnly(2026, 11, 2), 1150m, new AllocationLine(newer, 1150m));
        Assert.Equal("R-2", second.ReceiptNumber);
        Assert.Equal(0m, (await AccountAsync(_puneId)).Balance);
    }

    [Fact]
    public async Task Money_not_matched_to_bills_is_kept_as_advance()
    {
        var bill = await SellAsync(_puneId, new DateOnly(2026, 10, 1)); // 1,050

        var receipt = await ReceiveAsync(_puneId, new DateOnly(2026, 11, 1), 5000m, new AllocationLine(bill, 1050m));

        Assert.Equal(3950m, receipt.KeptAsAdvance);
        var account = await AccountAsync(_puneId);
        Assert.Equal(3950m, account.Advance);
        Assert.Equal(-3950m, account.Balance);
        Assert.Equal(account.Pending - account.Advance, account.Balance);
    }

    [Fact]
    public async Task Settling_more_than_pending_or_more_than_received_is_refused_and_nothing_is_saved()
    {
        var bill = await SellAsync(_puneId, new DateOnly(2026, 10, 1)); // 1,050
        var other = await SellAsync(_puneId, new DateOnly(2026, 10, 2)); // 1,050

        var tooMuch = await Assert.ThrowsAsync<ValidationException>(() =>
            ReceiveAsync(_puneId, new DateOnly(2026, 11, 1), 2000m, new AllocationLine(bill, 2000m)));
        Assert.Contains("only 1,050.00 is pending", tooMuch.Message);

        await Assert.ThrowsAsync<ValidationException>(() =>
            ReceiveAsync(_puneId, new DateOnly(2026, 11, 1), 1000m, new AllocationLine(bill, 1000m), new AllocationLine(other, 500m)));

        // Another customer's invoice cannot be settled.
        await Assert.ThrowsAsync<ValidationException>(() =>
            ReceiveAsync(_localId, new DateOnly(2026, 11, 1), 1050m, new AllocationLine(bill, 1050m)));

        Assert.Empty(await Receipts.SearchAsync(new ReceiptSearchCriteria()));
        Assert.Equal("R-1", (await ReceiveAsync(_puneId, new DateOnly(2026, 11, 1), 10m)).ReceiptNumber);
    }

    [Fact]
    public async Task Receipt_needs_customer_amount_date_and_cheque_number()
    {
        var future = DateOnly.FromDateTime(DateTime.Today).AddDays(2);

        await Assert.ThrowsAsync<ValidationException>(() => ReceiveAsync(0, new DateOnly(2026, 11, 1), 100m));
        await Assert.ThrowsAsync<ValidationException>(() => ReceiveAsync(_puneId, new DateOnly(2026, 11, 1), 0m));
        await Assert.ThrowsAsync<ValidationException>(() => ReceiveAsync(_puneId, new DateOnly(2026, 11, 1), 10.555m));
        await Assert.ThrowsAsync<ValidationException>(() => ReceiveAsync(_puneId, future, 100m));
        await Assert.ThrowsAsync<ValidationException>(() => Receipts.CreateAsync(new CreateReceiptRequest
        {
            CustomerId = _puneId, ReceiptDate = new DateOnly(2026, 11, 1), Amount = 100m, Mode = PaymentMode.Cheque
        }));
    }

    [Fact]
    public async Task Opening_bills_are_settled_first_and_opening_advance_is_used_before_newer_receipts()
    {
        await OpeningBills.AddAsync(new OpeningBillDto
        {
            CustomerId = _puneId, BillNumber = "T-145", BillDate = new DateOnly(2026, 3, 10), Side = BalanceSide.Debit, Amount = 20000m
        });
        await OpeningBills.AddAsync(new OpeningBillDto
        {
            CustomerId = _localId, BillNumber = "Advance", BillDate = new DateOnly(2026, 3, 31), Side = BalanceSide.Credit, Amount = 500m
        });

        var invoice = await SellAsync(_puneId, new DateOnly(2026, 10, 1));
        var account = await AccountAsync(_puneId);
        Assert.Equal(BillKind.OpeningBill, account.OpenBills[0].Ref.Kind);
        Assert.Equal("Old bill T-145", account.OpenBills[0].Number);
        Assert.Equal(new DateOnly(2026, 4, 9), account.OpenBills[0].DueDate);
        Assert.Equal(21050m, account.Balance);

        // Local Buyer: 500 old advance + a 3,000 receipt kept as advance, then a 2,100 invoice.
        await ReceiveAsync(_localId, new DateOnly(2026, 10, 5), 3000m);
        var localInvoice = await SellAsync(_localId, new DateOnly(2026, 10, 20), 2);
        Assert.Equal(3500m, (await AccountAsync(_localId)).Advance);

        await Receipts.AdjustAdvanceAsync(_localId, [new AllocationLine(localInvoice, 2100m)]);

        var local = await AccountAsync(_localId);
        Assert.Equal(0m, Bill(local, localInvoice).Pending);
        Assert.Equal(0m, local.Credits.Single(c => c.Ref.Kind == CreditKind.OpeningCredit).Unused);   // oldest used first
        Assert.Equal(1400m, local.Credits.Single(c => c.Ref.Kind == CreditKind.Receipt).Unused);
        Assert.Equal(-1400m, local.Balance);

        // More than the advance cannot be adjusted.
        var next = await SellAsync(_localId, new DateOnly(2026, 10, 25), 2);
        await Assert.ThrowsAsync<ValidationException>(() => Receipts.AdjustAdvanceAsync(_localId, [new AllocationLine(next, 2000m)]));
        _ = invoice;
    }

    [Fact]
    public async Task Opening_entry_in_use_cannot_be_removed()
    {
        var id = await OpeningBills.AddAsync(new OpeningBillDto
        {
            CustomerId = _puneId, BillNumber = "T-9", BillDate = new DateOnly(2026, 3, 1), Side = BalanceSide.Debit, Amount = 1000m
        });
        await ReceiveAsync(_puneId, new DateOnly(2026, 11, 1), 400m, new AllocationLine(new BillRef(BillKind.OpeningBill, id), 400m));

        var bills = await OpeningBills.GetAsync(_puneId);
        Assert.Equal(400m, Assert.Single(bills).Used);
        await Assert.ThrowsAsync<ValidationException>(() => OpeningBills.RemoveAsync(id));

        var unused = await OpeningBills.AddAsync(new OpeningBillDto
        {
            CustomerId = _puneId, BillNumber = "T-10", BillDate = new DateOnly(2026, 3, 2), Amount = 50m
        });
        await OpeningBills.RemoveAsync(unused);
        Assert.Single(await OpeningBills.GetAsync(_puneId));
    }

    [Fact]
    public async Task Cancelling_a_receipt_makes_its_bills_pending_again()
    {
        var bill = await SellAsync(_puneId, new DateOnly(2026, 10, 1));
        var receipt = await ReceiveAsync(_puneId, new DateOnly(2026, 11, 1), 1050m, new AllocationLine(bill, 1050m));
        Assert.Equal(0m, (await AccountAsync(_puneId)).Balance);

        await Assert.ThrowsAsync<ValidationException>(() => Receipts.CancelAsync(receipt.Id, " "));
        await Receipts.CancelAsync(receipt.Id, "Cheque bounced");

        var account = await AccountAsync(_puneId);
        Assert.Equal(1050m, Bill(account, bill).Pending);
        Assert.Equal(1050m, account.Balance);
        Assert.Empty(account.Credits);

        var detail = (await Receipts.GetAsync(receipt.Id))!;
        Assert.Equal(VoucherStatus.Cancelled, detail.Status);
        Assert.Equal("Cheque bounced", detail.CancellationReason);
        await Assert.ThrowsAsync<ValidationException>(() => Receipts.CancelAsync(receipt.Id, "Again"));
    }

    [Fact]
    public async Task Cancelling_an_invoice_with_money_received_turns_that_money_into_advance()
    {
        var bill = await SellAsync(_puneId, new DateOnly(2026, 10, 1)); // 1,050
        await ReceiveAsync(_puneId, new DateOnly(2026, 11, 1), 1050m, new AllocationLine(bill, 1050m));
        Assert.Equal(1050m, await Receipts.GetSettledAmountForInvoiceAsync(bill.Id));

        await Invoices.CancelAsync(bill.Id, "Wrong rate");

        var account = await AccountAsync(_puneId);
        Assert.Empty(account.Bills);
        Assert.Equal(1050m, account.Advance);
        Assert.Equal(-1050m, account.Balance);
        Assert.Equal(0m, await Receipts.GetSettledAmountForInvoiceAsync(bill.Id));
    }

    [Fact]
    public async Task Ledger_brings_forward_the_balance_and_keeps_a_running_balance()
    {
        await OpeningBills.AddAsync(new OpeningBillDto
        {
            CustomerId = _puneId, BillNumber = "T-1", BillDate = new DateOnly(2026, 3, 1), Amount = 12000m
        });
        var september = await SellAsync(_puneId, new DateOnly(2026, 9, 15), 10);           // 10,500 — before the period
        var receipt = await ReceiveAsync(_puneId, new DateOnly(2026, 10, 5), 15000m);    // credit
        await SellAsync(_puneId, new DateOnly(2026, 10, 20), 4);                          // 4,200
        var cancelled = await SellAsync(_puneId, new DateOnly(2026, 10, 21), 50);
        await Invoices.CancelAsync(cancelled.Id, "Mistake");
        _ = september;

        var ledger = await _host.Get<ICustomerLedgerService>().GetAsync(_puneId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.Equal(22500m, ledger.OpeningBalance);
        Assert.Collection(ledger.Entries,
            e => { Assert.Equal(LedgerEntryType.Receipt, e.Type); Assert.Equal(receipt.ReceiptNumber, e.Reference); Assert.Equal(7500m, e.Balance); },
            e => { Assert.Equal(LedgerEntryType.SalesInvoice, e.Type); Assert.Equal(4200m, e.Debit); Assert.Equal(11700m, e.Balance); });
        Assert.Equal(4200m, ledger.TotalDebit);
        Assert.Equal(15000m, ledger.TotalCredit);
        Assert.Equal(11700m, ledger.ClosingBalance);
        Assert.Equal("11,700.00 Dr", LedgerFormat.Balance(ledger.ClosingBalance));
        Assert.Equal("500.00 Cr", LedgerFormat.Balance(-500m));

        var csv = LedgerFormat.ToCsv(ledger);
        Assert.Contains("Opening balance,,,22500.00,Dr", csv);
        Assert.Contains("Closing balance,,,11700.00,Dr", csv);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _host.Get<ICustomerLedgerService>().GetAsync(_puneId, new DateOnly(2026, 11, 1), new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Csv_cells_cannot_start_a_formula()
    {
        var ledger = new CustomerLedger(1, "=HYPERLINK(\"x\")", "Pune", 30, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), 0, [], 0, 0, 0);

        var csv = LedgerFormat.ToCsv(ledger);

        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", csv);
    }

    [Fact]
    public async Task Outstanding_report_ages_pending_amounts_by_days_past_due()
    {
        // Due dates (30 days): Jul 31, Sep 30, Oct 31, Nov 30. As of 19 Nov: 111, 50 and 19 days late, and not due.
        await SellAsync(_puneId, new DateOnly(2026, 7, 1));      // 1,050  over 90
        await SellAsync(_puneId, new DateOnly(2026, 8, 31), 2);  // 2,100  31-60
        await SellAsync(_puneId, new DateOnly(2026, 10, 1), 3);  // 3,150  1-30
        await SellAsync(_puneId, new DateOnly(2026, 10, 31), 4); // 4,200  not due
        await ReceiveAsync(_localId, new DateOnly(2026, 11, 1), 700m); // advance only

        var accounts = new[] { (await AccountAsync(_puneId)), (await AccountAsync(_localId)) };
        var report = OutstandingReportService.Build(accounts, new DateOnly(2026, 11, 19));

        var pune = report.Rows[0];
        Assert.Equal("KATARIYA TEXTILES", pune.CustomerName);
        Assert.Equal(new AgeingBuckets(4200m, 3150m, 2100m, 0m, 1050m), pune.Ageing);
        Assert.Equal(10500m, pune.Pending);
        Assert.Equal(6300m, pune.Ageing.Overdue);

        var local = report.Rows[1];
        Assert.Equal(700m, local.Advance);
        Assert.Equal(-700m, local.Balance);
        Assert.Empty(local.Bills);

        Assert.Equal(9800m, report.TotalBalance);
        Assert.Contains("KATARIYA TEXTILES,Pune,10500.00,0.00,10500.00,Dr,4200.00,3150.00,2100.00,0.00,1050.00",
            OutstandingReportService.ToCsv(report));
    }

    [Fact]
    public async Task Dashboard_shows_collections_for_the_period_and_receivables_as_of_today()
    {
        var bill = await SellAsync(_puneId, new DateOnly(2026, 8, 1), 2);  // 2,100, overdue since Aug 31
        await ReceiveAsync(_puneId, new DateOnly(2026, 10, 10), 600m, new AllocationLine(bill, 600m));
        await ReceiveAsync(_localId, new DateOnly(2026, 9, 5), 250m);       // advance, before the period
        var cancelled = await ReceiveAsync(_localId, new DateOnly(2026, 10, 11), 999m);
        await Receipts.CancelAsync(cancelled.Id, "Entered twice");

        var summary = await _host.Get<IDashboardService>().GetSummaryAsync(new DateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));

        Assert.Equal(600m, summary.Collections);
        Assert.Equal(1, summary.ReceiptCount);
        Assert.Equal(1500m, summary.Receivables.Pending);
        Assert.Equal(250m, summary.Receivables.Advance);
        Assert.Equal(1500m, summary.Receivables.Overdue);
        var overdue = Assert.Single(summary.Receivables.MostOverdue);
        Assert.Equal("KATARIYA TEXTILES", overdue.CustomerName);
        Assert.Equal(new DateOnly(2026, 8, 31), overdue.DueDate);
        Assert.Equal([0m, 0m, 0m, 0m, 250m, 600m], summary.Trend.Select(m => m.Collections)); // May to October
    }

    [Fact]
    public async Task Receipt_statement_and_outstanding_report_render_as_pdf()
    {
        var bill = await SellAsync(_puneId, new DateOnly(2026, 10, 1), 3);
        var receipt = await ReceiveAsync(_puneId, new DateOnly(2026, 11, 1), 5000m, new AllocationLine(bill, 3150m));
        var printing = _host.Get<IAccountsPrintService>();

        var slip = await printing.ReceiptAsync(receipt.Id);
        var ledger = await _host.Get<ICustomerLedgerService>().GetAsync(_puneId, new DateOnly(2026, 4, 1), new DateOnly(2026, 11, 18));
        var statement = await printing.StatementAsync(ledger);
        var outstanding = await printing.OutstandingAsync(OutstandingReportService.Build([await AccountAsync(_puneId)], new DateOnly(2026, 11, 18)));

        foreach (var (doc, name) in new[] { (slip, "receipt"), (statement, "statement"), (outstanding, "outstanding") })
        {
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(doc.Pdf, 0, 4));
            Assert.NotEmpty(doc.PageImages);
            KeepForVisualCheck(doc, name);
        }

        Assert.Equal("Receipt-R-1.pdf", slip.SuggestedFileName);
    }

    /// <summary>Set SAREEBILLING_PDF_OUT to a folder to keep the PDFs and page images for a visual check.</summary>
    private static void KeepForVisualCheck(DocumentPrintout doc, string name)
    {
        var folder = Environment.GetEnvironmentVariable("SAREEBILLING_PDF_OUT");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), doc.Pdf);
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), doc.PageImages[0]);
    }
}
