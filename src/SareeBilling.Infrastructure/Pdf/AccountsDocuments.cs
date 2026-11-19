using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SareeBilling.Application.Receivables;
using SareeBilling.Domain.Common;
using SareeBilling.Domain.Entities;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Infrastructure.Pdf;

internal sealed class QuestPdfAccountsRenderer : IAccountsPdfRenderer
{
    static QuestPdfAccountsRenderer()
    {
        // Same licence decision as invoices (Community: annual turnover under US$1M).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public (byte[] Pdf, IReadOnlyList<byte[]> PageImages) RenderReceipt(CompanyHeader company, ReceiptDetail receipt, string amountInWords, int dpi) =>
        Render(new ReceiptDocument(company, receipt, amountInWords), dpi);

    public (byte[] Pdf, IReadOnlyList<byte[]> PageImages) RenderStatement(CompanyHeader company, CustomerLedger ledger, int dpi) =>
        Render(new StatementDocument(company, ledger), dpi);

    public (byte[] Pdf, IReadOnlyList<byte[]> PageImages) RenderOutstanding(CompanyHeader company, OutstandingReport report, int dpi) =>
        Render(new OutstandingDocument(company, report), dpi);

    public (byte[] Pdf, IReadOnlyList<byte[]> PageImages) RenderCreditNote(CreditNote note, byte[]? logo, int dpi) =>
        Render(InvoicePdfDocument.ForCreditNote(note, logo), dpi);

    private static (byte[], IReadOnlyList<byte[]>) Render(IDocument document, int dpi) =>
        (document.GeneratePdf(),
         document.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = dpi }).ToList());
}

/// <summary>Shared look: company block at the top, page numbers at the bottom, A4 portrait.</summary>
internal abstract class AccountsDocument(CompanyHeader company) : IDocument
{
    protected const float Line = 0.6f;
    protected const string Muted = "#555555";
    protected const string HeaderFill = "#EEEEEE";

    protected CompanyHeader Company { get; } = company;

    protected abstract string DocumentTitle { get; }

    public DocumentMetadata GetMetadata() => new()
    {
        Title = DocumentTitle,
        Author = Company.Name,
        Creator = "Saree Billing",
        Producer = "Saree Billing"
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(t => t.FontSize(9).FontColor(Colors.Black));
            page.Header().Element(ComposeCompany);
            page.Content().PaddingTop(10).Element(ComposeBody);
            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Muted));
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
                t.Span("  ·  Computer-generated document");
            });
        });
    }

    protected abstract void ComposeBody(IContainer container);

    private void ComposeCompany(IContainer container)
    {
        container.BorderBottom(Line).PaddingBottom(6).Column(c =>
        {
            c.Item().AlignCenter().Text(Company.Name).Bold().FontSize(14);
            c.Item().AlignCenter().Text(Company.Address).FontSize(8.5f);
            var contact = string.Join("   ", new[]
            {
                string.IsNullOrWhiteSpace(Company.Gstin) ? null : "GSTIN: " + Company.Gstin,
                string.IsNullOrWhiteSpace(Company.Phone) ? null : "Phone: " + Company.Phone
            }.Where(s => s is not null));
            if (contact.Length > 0)
            {
                c.Item().AlignCenter().Text(contact).FontSize(8.5f);
            }

            c.Item().PaddingTop(6).AlignCenter().Text(DocumentTitle).Bold().FontSize(12);
        });
    }

    protected static IContainer HeadCell(IContainer cell) =>
        cell.Background(HeaderFill).BorderBottom(Line).PaddingVertical(4).PaddingHorizontal(4).DefaultTextStyle(t => t.Bold());

    protected static IContainer BodyCell(IContainer cell) =>
        cell.BorderBottom(0.3f).BorderColor("#CCCCCC").PaddingVertical(3).PaddingHorizontal(4);

    protected static string Amount(decimal value) => value == 0 ? string.Empty : IndianFormat.Amount(value);
}

internal sealed class ReceiptDocument(CompanyHeader company, ReceiptDetail receipt, string amountInWords) : AccountsDocument(company)
{
    protected override string DocumentTitle => "Payment Receipt";

    protected override void ComposeBody(IContainer container)
    {
        container.Column(column =>
        {
            if (receipt.Status == VoucherStatus.Cancelled)
            {
                column.Item().Background("#FDECEE").Padding(6).Text($"CANCELLED — {receipt.CancellationReason}").Bold().FontColor("#8A1C2B");
            }

            column.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text(t => { t.Span("Receipt no.: ").FontColor(Muted); t.Span(receipt.ReceiptNumber).Bold(); });
                row.RelativeItem().AlignRight().Text(t => { t.Span("Date: ").FontColor(Muted); t.Span(receipt.ReceiptDate.ToString("dd-MMM-yyyy")).Bold(); });
            });

            column.Item().PaddingTop(12).Text(t =>
            {
                t.Span("Received with thanks from ").FontColor(Muted);
                t.Span(receipt.CustomerName).Bold().FontSize(11);
            });
            column.Item().Text(receipt.CustomerAddress).FontSize(8.5f);
            if (!string.IsNullOrWhiteSpace(receipt.CustomerGstin))
            {
                column.Item().Text("GSTIN: " + receipt.CustomerGstin).FontSize(8.5f);
            }

            column.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("The sum of ").FontColor(Muted);
                    t.Span(amountInWords).Italic();
                });
                row.ConstantItem(150).Border(Line).Padding(6).AlignRight().Text("₹ " + IndianFormat.Amount(receipt.Amount)).Bold().FontSize(13);
            });

            var mode = PaymentModes.Label(receipt.Mode) + (string.IsNullOrWhiteSpace(receipt.Reference) ? "" : " · " + receipt.Reference);
            column.Item().PaddingTop(6).Text(t => { t.Span("Paid by: ").FontColor(Muted); t.Span(mode); });

            if (receipt.Allocations.Count > 0)
            {
                column.Item().PaddingTop(10).Text("Settled against").Bold();
                column.Item().PaddingTop(2).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                    });
                    table.Header(h =>
                    {
                        h.Cell().Element(HeadCell).Text("Bill");
                        h.Cell().Element(HeadCell).Text("Bill date");
                        h.Cell().Element(HeadCell).AlignRight().Text("Amount (₹)");
                    });
                    foreach (var a in receipt.Allocations)
                    {
                        table.Cell().Element(BodyCell).Text(a.BillNumber);
                        table.Cell().Element(BodyCell).Text(a.BillDate.ToString("dd-MMM-yyyy"));
                        table.Cell().Element(BodyCell).AlignRight().Text(IndianFormat.Amount(a.Amount));
                    }
                });
            }

            if (receipt.KeptAsAdvance > 0)
            {
                column.Item().PaddingTop(4).Text($"Kept as advance: ₹ {IndianFormat.Amount(receipt.KeptAsAdvance)}");
            }

            if (!string.IsNullOrWhiteSpace(receipt.Narration))
            {
                column.Item().PaddingTop(6).Text(t => { t.Span("Note: ").FontColor(Muted); t.Span(receipt.Narration); });
            }

            column.Item().PaddingTop(36).AlignRight().Column(sign =>
            {
                sign.Item().AlignRight().Text("For " + Company.Name).Bold();
                sign.Item().PaddingTop(28).AlignRight().Text("Authorised Signatory").FontColor(Muted);
            });
        });
    }
}

internal sealed class StatementDocument(CompanyHeader company, CustomerLedger ledger) : AccountsDocument(company)
{
    protected override string DocumentTitle => "Statement of Account";

    protected override void ComposeBody(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(ledger.CustomerName).Bold().FontSize(11);
                    c.Item().Text(ledger.City).FontColor(Muted);
                });
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text($"Period: {LedgerFormat.Date(ledger.From)} to {LedgerFormat.Date(ledger.To)}");
                    c.Item().AlignRight().Text($"Closing balance: {LedgerFormat.Balance(ledger.ClosingBalance)}").Bold();
                });
            });

            column.Item().PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(58);
                    c.ConstantColumn(70);
                    c.ConstantColumn(64);
                    c.RelativeColumn();
                    c.ConstantColumn(66);
                    c.ConstantColumn(66);
                    c.ConstantColumn(78);
                });

                table.Header(h =>
                {
                    h.Cell().Element(HeadCell).Text("Date");
                    h.Cell().Element(HeadCell).Text("Type");
                    h.Cell().Element(HeadCell).Text("Ref. no.");
                    h.Cell().Element(HeadCell).Text("Particulars");
                    h.Cell().Element(HeadCell).AlignRight().Text("Debit");
                    h.Cell().Element(HeadCell).AlignRight().Text("Credit");
                    h.Cell().Element(HeadCell).AlignRight().Text("Balance");
                });

                table.Cell().Element(BodyCell).Text(LedgerFormat.Date(ledger.From)).Italic();
                table.Cell().ColumnSpan(5).Element(BodyCell).Text("Opening balance").Italic();
                table.Cell().Element(BodyCell).AlignRight().Text(LedgerFormat.Balance(ledger.OpeningBalance)).Italic();

                foreach (var e in ledger.Entries)
                {
                    table.Cell().Element(BodyCell).Text(LedgerFormat.Date(e.Date));
                    table.Cell().Element(BodyCell).Text(LedgerFormat.TypeLabel(e.Type));
                    table.Cell().Element(BodyCell).Text(e.Reference);
                    table.Cell().Element(BodyCell).Text(e.Particulars).FontSize(8);
                    table.Cell().Element(BodyCell).AlignRight().Text(Amount(e.Debit));
                    table.Cell().Element(BodyCell).AlignRight().Text(Amount(e.Credit));
                    table.Cell().Element(BodyCell).AlignRight().Text(LedgerFormat.Balance(e.Balance));
                }

                table.Cell().ColumnSpan(4).Element(HeadCell).Text("Totals for the period");
                table.Cell().Element(HeadCell).AlignRight().Text(IndianFormat.Amount(ledger.TotalDebit));
                table.Cell().Element(HeadCell).AlignRight().Text(IndianFormat.Amount(ledger.TotalCredit));
                table.Cell().Element(HeadCell).AlignRight().Text(LedgerFormat.Balance(ledger.ClosingBalance));
            });

            column.Item().PaddingTop(8).Text("Dr = amount payable by the customer. Cr = advance held for the customer.")
                .FontSize(7.5f).FontColor(Muted);
        });
    }
}

internal sealed class OutstandingDocument(CompanyHeader company, OutstandingReport report) : AccountsDocument(company)
{
    protected override string DocumentTitle => "Outstanding Receivables";

    protected override void ComposeBody(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().AlignRight().Text($"As of {LedgerFormat.Date(report.AsOf)}  ·  amounts in ₹  ·  ageing by days past the due date");

            column.Item().PaddingTop(8).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(1.6f);
                    c.RelativeColumn(1.6f);
                    c.RelativeColumn(1.6f);
                    c.RelativeColumn(1.6f);
                    c.RelativeColumn(1.6f);
                    c.RelativeColumn(1.6f);
                    c.RelativeColumn(1.9f);
                });

                table.Header(h =>
                {
                    h.Cell().Element(HeadCell).Text("Customer");
                    h.Cell().Element(HeadCell).AlignRight().Text("Not due");
                    h.Cell().Element(HeadCell).AlignRight().Text("1–30");
                    h.Cell().Element(HeadCell).AlignRight().Text("31–60");
                    h.Cell().Element(HeadCell).AlignRight().Text("61–90");
                    h.Cell().Element(HeadCell).AlignRight().Text("Over 90");
                    h.Cell().Element(HeadCell).AlignRight().Text("Advance");
                    h.Cell().Element(HeadCell).AlignRight().Text("Balance");
                });

                foreach (var r in report.Rows)
                {
                    table.Cell().Element(BodyCell).Text(t =>
                    {
                        t.Span(r.CustomerName).Bold();
                        t.Span("  " + r.City).FontSize(7.5f).FontColor(Muted);
                    });
                    table.Cell().Element(BodyCell).AlignRight().Text(Amount(r.Ageing.NotDue));
                    table.Cell().Element(BodyCell).AlignRight().Text(Amount(r.Ageing.Days1To30));
                    table.Cell().Element(BodyCell).AlignRight().Text(Amount(r.Ageing.Days31To60));
                    table.Cell().Element(BodyCell).AlignRight().Text(Amount(r.Ageing.Days61To90));
                    table.Cell().Element(BodyCell).AlignRight().Text(Amount(r.Ageing.Over90));
                    table.Cell().Element(BodyCell).AlignRight().Text(Amount(r.Advance));
                    table.Cell().Element(BodyCell).AlignRight().Text(LedgerFormat.Balance(r.Balance)).Bold();
                }

                var t = report.TotalAgeing;
                table.Cell().Element(HeadCell).Text("Total");
                table.Cell().Element(HeadCell).AlignRight().Text(IndianFormat.Amount(t.NotDue));
                table.Cell().Element(HeadCell).AlignRight().Text(IndianFormat.Amount(t.Days1To30));
                table.Cell().Element(HeadCell).AlignRight().Text(IndianFormat.Amount(t.Days31To60));
                table.Cell().Element(HeadCell).AlignRight().Text(IndianFormat.Amount(t.Days61To90));
                table.Cell().Element(HeadCell).AlignRight().Text(IndianFormat.Amount(t.Over90));
                table.Cell().Element(HeadCell).AlignRight().Text(IndianFormat.Amount(report.TotalAdvance));
                table.Cell().Element(HeadCell).AlignRight().Text(LedgerFormat.Balance(report.TotalBalance));
            });
        });
    }
}
