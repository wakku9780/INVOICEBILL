using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SareeBilling.Domain.Common;
using SareeBilling.Domain.Entities;
using SareeBilling.Domain.Enums;
using SareeBilling.Domain.Gst;

namespace SareeBilling.Infrastructure.Pdf;

/// <summary>
/// A4 tax invoice in the familiar Tally layout: one outer box; seller and buyer on the left,
/// invoice references on the right; item table with GST lines; amount in words;
/// HSN-wise tax summary; bank details, declaration and signatory.
/// Everything printed comes from the stored invoice snapshot, never from current settings
/// (the logo is the only exception).
/// </summary>
internal sealed class InvoicePdfDocument : IDocument
{
    private const float Line = 0.8f;
    private const float SmallSize = 7.5f;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly Invoice _invoice;
    private readonly byte[]? _logo;
    private readonly IReadOnlyList<InvoiceItem> _items;
    private readonly bool _isIntraState;

    /// <summary>Set when this document prints a credit note (the invoice fields then hold the credit note's figures).</summary>
    private readonly CreditNoteHeading? _creditNote;

    public InvoicePdfDocument(Invoice invoice, byte[]? logo)
        : this(invoice, logo, null)
    {
    }

    private InvoicePdfDocument(Invoice invoice, byte[]? logo, CreditNoteHeading? creditNote)
    {
        _invoice = invoice;
        _logo = logo is { Length: > 0 } ? logo : null;
        _items = invoice.Items.OrderBy(i => i.LineNumber).ToList();
        _isIntraState = invoice.TaxType == TaxType.IntraState;
        _creditNote = creditNote;
    }

    private sealed record CreditNoteHeading(
        string Number, DateOnly Date, CreditNoteKind Kind, string Reason, bool IsCancelled, string? CancellationReason);

    /// <summary>
    /// A GST credit note in the same layout as the tax invoice: the same seller, buyer, GST type and
    /// HSN summary, headed "Credit Note" with the original invoice number and date.
    /// </summary>
    public static InvoicePdfDocument ForCreditNote(CreditNote note, byte[]? logo)
    {
        ArgumentNullException.ThrowIfNull(note);

        var invoice = new Invoice
        {
            InvoiceNumber = note.InvoiceNumber,
            InvoiceDate = note.InvoiceDate,
            SellerName = note.SellerName,
            SellerAddress = note.SellerAddress,
            SellerGstin = note.SellerGstin,
            SellerState = note.SellerState,
            SellerStateCode = note.SellerStateCode,
            SellerPhone = note.SellerPhone,
            SellerEmail = note.SellerEmail,
            BuyerName = note.BuyerName,
            BuyerAddress = note.BuyerAddress,
            BuyerGstin = note.BuyerGstin,
            BuyerState = note.BuyerState,
            BuyerStateCode = note.BuyerStateCode,
            BuyerPhone = note.BuyerPhone,
            TaxType = note.TaxType,
            TotalQuantity = note.TotalQuantity,
            TaxableTotal = note.TaxableTotal,
            CgstTotal = note.CgstTotal,
            SgstTotal = note.SgstTotal,
            IgstTotal = note.IgstTotal,
            TotalTax = note.TotalTax,
            RoundOff = note.RoundOff,
            GrandTotal = note.GrandTotal,
            AmountInWords = note.AmountInWords,
            Items = note.Items.Select(i => new InvoiceItem
            {
                LineNumber = i.LineNumber,
                Description = i.Description,
                HsnCode = i.HsnCode,
                Quantity = i.Quantity,
                Unit = i.Unit,
                Rate = i.Rate,
                TaxableAmount = i.TaxableAmount,
                GstRate = i.GstRate,
                CgstRate = i.CgstRate,
                CgstAmount = i.CgstAmount,
                SgstRate = i.SgstRate,
                SgstAmount = i.SgstAmount,
                IgstRate = i.IgstRate,
                IgstAmount = i.IgstAmount,
                LineTotal = i.LineTotal
            }).ToList()
        };

        return new InvoicePdfDocument(invoice, logo, new CreditNoteHeading(
            note.CreditNoteNumber, note.CreditNoteDate, note.Kind, note.Reason,
            note.Status == VoucherStatus.Cancelled, note.CancellationReason));
    }

    private bool IsCreditNote => _creditNote is not null;

    private bool IsCancelled => _creditNote?.IsCancelled ?? _invoice.Status == InvoiceStatus.Cancelled;

    private string? CancellationReason => _creditNote is not null ? _creditNote.CancellationReason : _invoice.CancellationReason;

    private string DocumentTitle => IsCreditNote ? "Credit Note" : "Tax Invoice";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"{DocumentTitle} {_creditNote?.Number ?? _invoice.InvoiceNumber}",
        Author = _invoice.SellerName,
        Subject = $"{DocumentTitle} for {_invoice.BuyerName}",
        Creator = "Saree Billing",
        Producer = "Saree Billing"
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(24);
            // Lato ships inside the QuestPDF package, so the invoice looks the same on every computer.
            page.DefaultTextStyle(t => t.FontSize(8.5f).FontColor(Colors.Black));

            page.Header().Element(ComposeTitle);

            // ShrinkVertical: the outer box ends after the signature instead of running to the page bottom.
            page.Content().PaddingTop(4).ShrinkVertical().Border(Line).Column(column =>
            {
                column.Item().Element(ComposeParties);
                column.Item().Element(ComposeItems);
                column.Item().Element(ComposeAmountInWords);
                column.Item().Element(ComposeHsnSummary);
                column.Item().Element(ComposeBankAndSignature);
            });

            page.Footer().PaddingTop(4).AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(t => t.FontSize(SmallSize));
                text.Span(IsCreditNote ? "This is a Computer Generated Credit Note" : "This is a Computer Generated Invoice");
                text.Span("        Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });

            if (IsCancelled)
            {
                // Semi-transparent red (ARGB) so the figures underneath stay readable.
                page.Foreground().AlignCenter().AlignMiddle().Rotate(-35)
                    .Text("CANCELLED").FontSize(90).Bold().FontColor("#40D32F2F");
            }
        });
    }

    // ---------------------------------------------------------------- title

    private void ComposeTitle(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().AlignCenter().Text(DocumentTitle).Bold().FontSize(13);

            if (IsCancelled)
            {
                var reason = string.IsNullOrWhiteSpace(CancellationReason) ? string.Empty : $" - {CancellationReason}";
                column.Item().AlignCenter().Text($"CANCELLED{reason}").Bold().FontColor(Colors.Red.Darken2);
            }

            // From page 2 onwards, say which document the page belongs to.
            var continued = _creditNote is { } cn
                ? $"Credit Note No. {cn.Number} dated {FormatDate(cn.Date)} (continued)"
                : $"Invoice No. {_invoice.InvoiceNumber} dated {FormatDate(_invoice.InvoiceDate)} (continued)";
            column.Item().SkipOnce().AlignCenter().Text(continued).FontSize(SmallSize).Italic();
        });
    }

    // ---------------------------------------------------------------- seller / buyer / references

    private void ComposeParties(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().BorderRight(Line).Column(left =>
            {
                left.Item().Padding(4).Element(ComposeSeller);
                left.Item().BorderTop(Line).Padding(4).Element(ComposeBuyer);
            });

            row.RelativeItem().Element(ComposeReferences);
        });
    }

    private void ComposeSeller(IContainer container)
    {
        container.Row(row =>
        {
            if (_logo is not null)
            {
                row.ConstantItem(54).Height(54).AlignMiddle().Image(_logo).FitArea();
                row.ConstantItem(6);
            }

            row.RelativeItem().Column(c =>
            {
                c.Item().Text(_invoice.SellerName).Bold().FontSize(11);
                foreach (var line in Lines(_invoice.SellerAddress))
                {
                    c.Item().Text(line);
                }

                c.Item().Text($"GSTIN/UIN: {_invoice.SellerGstin}");
                c.Item().Text($"State Name: {_invoice.SellerState}, Code: {_invoice.SellerStateCode}");
                if (!string.IsNullOrWhiteSpace(_invoice.SellerPhone))
                {
                    c.Item().Text($"Phone: {_invoice.SellerPhone}");
                }

                if (!string.IsNullOrWhiteSpace(_invoice.SellerEmail))
                {
                    c.Item().Text($"E-Mail: {_invoice.SellerEmail}");
                }
            });
        });
    }

    private void ComposeBuyer(IContainer container)
    {
        container.Column(c =>
        {
            c.Item().Text("Buyer (Bill to)").FontSize(SmallSize).Italic();
            c.Item().Text(_invoice.BuyerName).Bold().FontSize(10);
            foreach (var line in Lines(_invoice.BuyerAddress))
            {
                c.Item().Text(line);
            }

            c.Item().Text($"GSTIN/UIN: {(string.IsNullOrWhiteSpace(_invoice.BuyerGstin) ? "Unregistered" : _invoice.BuyerGstin)}");
            c.Item().Text($"State Name: {_invoice.BuyerState}, Code: {_invoice.BuyerStateCode}");
            if (!string.IsNullOrWhiteSpace(_invoice.BuyerPhone))
            {
                c.Item().Text($"Phone: {_invoice.BuyerPhone}");
            }
        });
    }

    private void ComposeReferences(IContainer container)
    {
        if (_creditNote is { } cn)
        {
            container.Column(c =>
            {
                ReferencePair(c, "Credit Note No.", cn.Number, "Dated", FormatDate(cn.Date));
                ReferencePair(c, "Original Invoice No.", _invoice.InvoiceNumber, "Invoice Dated", FormatDate(_invoice.InvoiceDate));
                ReferencePair(c, "Type", cn.Kind == CreditNoteKind.GoodsReturn ? "Sales return (goods received back)" : "Reduction in value",
                    "Place of Supply", $"{_invoice.BuyerState} ({_invoice.BuyerStateCode})");
                c.Item().Padding(3).Column(cell =>
                {
                    cell.Item().Text("Reason").FontSize(SmallSize);
                    cell.Item().MinHeight(12).Text(cn.Reason).Bold();
                });
            });
            return;
        }

        container.Column(c =>
        {
            ReferencePair(c, "Invoice No.", _invoice.InvoiceNumber, "Dated", FormatDate(_invoice.InvoiceDate));
            ReferencePair(c, "Buyer's Order No.", _invoice.BuyerOrderNumber,
                "Dated", _invoice.BuyerOrderDate is { } orderDate ? FormatDate(orderDate) : null);
            ReferencePair(c, "Mode/Terms of Payment", _invoice.PaymentTerms, "Destination", _invoice.Destination);
            ReferencePair(c, "Despatch Document No.", _invoice.DispatchDocumentNumber, "Despatched through", _invoice.DispatchedThrough);

            c.Item().Padding(3).Column(cell =>
            {
                cell.Item().Text("Terms of Delivery").FontSize(SmallSize);
                cell.Item().MinHeight(12).Text(_invoice.TermsOfDelivery ?? string.Empty).Bold();
            });
        });
    }

    private static void ReferencePair(ColumnDescriptor column, string label1, string? value1, string label2, string? value2)
    {
        column.Item().BorderBottom(Line).Row(row =>
        {
            row.RelativeItem().BorderRight(Line).Padding(3).Element(c => ReferenceField(c, label1, value1));
            row.RelativeItem().Padding(3).Element(c => ReferenceField(c, label2, value2));
        });
    }

    private static void ReferenceField(IContainer container, string label, string? value)
    {
        container.Column(c =>
        {
            c.Item().Text(label).FontSize(SmallSize);
            c.Item().MinHeight(11).Text(value ?? string.Empty).Bold();
        });
    }

    // ---------------------------------------------------------------- items

    private void ComposeItems(IContainer container)
    {
        container.BorderTop(Line).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(24);   // Sl
                columns.RelativeColumn();     // Description
                columns.ConstantColumn(46);   // HSN
                columns.ConstantColumn(62);   // Quantity
                columns.ConstantColumn(62);   // Rate
                columns.ConstantColumn(30);   // per
                columns.ConstantColumn(78);   // Amount
            });

            table.Header(header =>
            {
                string[] titles = ["Sl No.", "Description of Goods", "HSN/SAC", "Quantity", "Rate", "per", "Amount"];
                for (var i = 0; i < titles.Length; i++)
                {
                    var cell = header.Cell().BorderBottom(Line);
                    if (i < titles.Length - 1)
                    {
                        cell = cell.BorderRight(Line);
                    }

                    cell.Padding(3).AlignCenter().Text(titles[i]).Bold();
                }
            });

            foreach (var item in _items)
            {
                Cell(table).AlignCenter().Text(item.LineNumber.ToString(Invariant));
                Cell(table).Text(item.Description).Bold();
                Cell(table).AlignCenter().Text(item.HsnCode);
                // A reduction in value (no goods back) has no quantity or rate.
                var hasQuantity = item.Quantity != 0;
                Cell(table).AlignRight().Text(hasQuantity ? $"{IndianFormat.Quantity(item.Quantity)} {item.Unit}" : string.Empty).Bold();
                Cell(table).AlignRight().Text(hasQuantity ? IndianFormat.Amount(item.Rate) : string.Empty);
                Cell(table).AlignCenter().Text(hasQuantity ? item.Unit : string.Empty);
                Cell(table, last: true).AlignRight().Text(IndianFormat.Amount(item.TaxableAmount)).Bold();
            }

            foreach (var (label, amount) in TaxRows())
            {
                Cell(table, topPadding: 6);
                Cell(table, topPadding: 6).AlignRight().Text(label).Bold().Italic();
                Cell(table, topPadding: 6);
                Cell(table, topPadding: 6);
                Cell(table, topPadding: 6);
                Cell(table, topPadding: 6);
                Cell(table, last: true, topPadding: 6).AlignRight().Text(IndianFormat.Amount(amount)).Bold();
            }

            if (_invoice.RoundOff != 0m)
            {
                Cell(table);
                Cell(table).AlignRight().Text("Round Off").Italic();
                Cell(table);
                Cell(table);
                Cell(table);
                Cell(table);
                Cell(table, last: true).AlignRight().Text(IndianFormat.Amount(_invoice.RoundOff));
            }

            // Leaves some empty ruled space under short invoices, like Tally.
            var spacer = Math.Max(0, 120 - (_items.Count * 14));
            for (var i = 0; i < 7; i++)
            {
                Cell(table, last: i == 6).Height(spacer);
            }

            TotalCell(table);
            TotalCell(table).AlignRight().Text("Total").Bold();
            TotalCell(table);
            TotalCell(table).AlignRight().Text(TotalQuantityText()).Bold();
            TotalCell(table);
            TotalCell(table);
            TotalCell(table, last: true).AlignRight().Text(IndianFormat.Amount(_invoice.GrandTotal)).Bold().FontSize(10);
        });
    }

    /// <summary>One tax line per GST rate, e.g. "IGST @ 5%" or "CGST @ 2.5%" + "SGST @ 2.5%".</summary>
    private IEnumerable<(string Label, decimal Amount)> TaxRows()
    {
        foreach (var group in _items.GroupBy(i => i.GstRate).OrderBy(g => g.Key))
        {
            if (_isIntraState)
            {
                var half = IndianFormat.Percent(group.First().CgstRate);
                yield return ($"CGST @ {half}%", group.Sum(i => i.CgstAmount));
                yield return ($"SGST @ {half}%", group.Sum(i => i.SgstAmount));
            }
            else
            {
                yield return ($"IGST @ {IndianFormat.Percent(group.Key)}%", group.Sum(i => i.IgstAmount));
            }
        }
    }

    private string TotalQuantityText()
    {
        if (_invoice.TotalQuantity == 0)
        {
            return string.Empty;
        }

        var units = _items.Select(i => i.Unit).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var quantity = IndianFormat.Quantity(_invoice.TotalQuantity);
        return units.Count == 1 ? $"{quantity} {units[0]}" : quantity;
    }

    private static IContainer Cell(TableDescriptor table, bool last = false, float topPadding = 2)
    {
        IContainer cell = table.Cell();
        if (!last)
        {
            cell = cell.BorderRight(Line);
        }

        return cell.PaddingHorizontal(3).PaddingTop(topPadding).PaddingBottom(2);
    }

    private static IContainer TotalCell(TableDescriptor table, bool last = false)
    {
        var cell = table.Cell().BorderTop(Line);
        if (!last)
        {
            cell = cell.BorderRight(Line);
        }

        return cell.PaddingHorizontal(3).PaddingVertical(3);
    }

    // ---------------------------------------------------------------- amount in words

    private void ComposeAmountInWords(IContainer container)
    {
        container.BorderTop(Line).Padding(4).Row(row =>
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().Text(IsCreditNote ? "Amount Credited (in words)" : "Amount Chargeable (in words)").FontSize(SmallSize);
                c.Item().Text(_invoice.AmountInWords).Bold();
            });
            row.AutoItem().AlignBottom().Text("E. & O.E").Italic();
        });
    }

    // ---------------------------------------------------------------- HSN summary

    private void ComposeHsnSummary(IContainer container)
    {
        var summary = HsnTaxSummary.From(_items);

        container.BorderTop(Line).Column(column =>
        {
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);      // HSN
                    columns.RelativeColumn(2);      // Taxable
                    if (_isIntraState)
                    {
                        columns.ConstantColumn(40); // CGST rate
                        columns.RelativeColumn(1.5f);
                        columns.ConstantColumn(40); // SGST rate
                        columns.RelativeColumn(1.5f);
                    }
                    else
                    {
                        columns.ConstantColumn(40); // IGST rate
                        columns.RelativeColumn(1.5f);
                    }

                    columns.RelativeColumn(1.5f);   // Total tax
                });

                table.Header(header =>
                {
                    var taxNames = _isIntraState ? new[] { "CGST", "SGST" } : new[] { "IGST" };

                    header.Cell().RowSpan(2).Element(HeaderCell).AlignCenter().AlignMiddle().Text("HSN/SAC").Bold();
                    header.Cell().RowSpan(2).Element(HeaderCell).AlignCenter().AlignMiddle().Text("Taxable Value").Bold();
                    foreach (var name in taxNames)
                    {
                        header.Cell().ColumnSpan(2).Element(HeaderCell).AlignCenter().Text(name).Bold();
                    }

                    header.Cell().RowSpan(2).Element(c => HeaderCell(c, last: true)).AlignCenter().AlignMiddle().Text("Total Tax Amount").Bold();

                    foreach (var _ in taxNames)
                    {
                        header.Cell().Element(HeaderCell).AlignCenter().Text("Rate").Bold();
                        header.Cell().Element(HeaderCell).AlignCenter().Text("Amount").Bold();
                    }
                });

                foreach (var line in summary)
                {
                    SummaryCell(table).Text(line.HsnCode);
                    SummaryCell(table).AlignRight().Text(IndianFormat.Amount(line.TaxableAmount));
                    if (_isIntraState)
                    {
                        SummaryCell(table).AlignRight().Text($"{IndianFormat.Percent(line.CgstRate)}%");
                        SummaryCell(table).AlignRight().Text(IndianFormat.Amount(line.CgstAmount));
                        SummaryCell(table).AlignRight().Text($"{IndianFormat.Percent(line.SgstRate)}%");
                        SummaryCell(table).AlignRight().Text(IndianFormat.Amount(line.SgstAmount));
                    }
                    else
                    {
                        SummaryCell(table).AlignRight().Text($"{IndianFormat.Percent(line.IgstRate)}%");
                        SummaryCell(table).AlignRight().Text(IndianFormat.Amount(line.IgstAmount));
                    }

                    SummaryCell(table, last: true).AlignRight().Text(IndianFormat.Amount(line.TotalTax));
                }

                SummaryTotalCell(table).AlignRight().Text("Total").Bold();
                SummaryTotalCell(table).AlignRight().Text(IndianFormat.Amount(_invoice.TaxableTotal)).Bold();
                if (_isIntraState)
                {
                    SummaryTotalCell(table);
                    SummaryTotalCell(table).AlignRight().Text(IndianFormat.Amount(_invoice.CgstTotal)).Bold();
                    SummaryTotalCell(table);
                    SummaryTotalCell(table).AlignRight().Text(IndianFormat.Amount(_invoice.SgstTotal)).Bold();
                }
                else
                {
                    SummaryTotalCell(table);
                    SummaryTotalCell(table).AlignRight().Text(IndianFormat.Amount(_invoice.IgstTotal)).Bold();
                }

                SummaryTotalCell(table, last: true).AlignRight().Text(IndianFormat.Amount(_invoice.TotalTax)).Bold();
            });

            column.Item().BorderTop(Line).Padding(4).Text(text =>
            {
                text.Span("Tax Amount (in words): ").FontSize(SmallSize);
                text.Span(AmountInWords.ToRupees(_invoice.TotalTax)).Bold();
            });
        });
    }

    private static IContainer HeaderCell(IContainer container) => HeaderCell(container, last: false);

    private static IContainer HeaderCell(IContainer container, bool last)
    {
        var cell = container.BorderBottom(Line);
        if (!last)
        {
            cell = cell.BorderRight(Line);
        }

        return cell.Padding(2).DefaultTextStyle(t => t.FontSize(SmallSize));
    }

    private static IContainer SummaryCell(TableDescriptor table, bool last = false)
    {
        IContainer cell = table.Cell();
        if (!last)
        {
            cell = cell.BorderRight(Line);
        }

        return cell.PaddingHorizontal(3).PaddingVertical(2);
    }

    private static IContainer SummaryTotalCell(TableDescriptor table, bool last = false)
    {
        var cell = table.Cell().BorderTop(Line);
        if (!last)
        {
            cell = cell.BorderRight(Line);
        }

        return cell.PaddingHorizontal(3).PaddingVertical(2);
    }

    // ---------------------------------------------------------------- bank, declaration, signature

    private void ComposeBankAndSignature(IContainer container)
    {
        container.BorderTop(Line).ShowEntire().Row(row =>
        {
            row.RelativeItem().Padding(4).Column(c =>
            {
                if (IsCreditNote)
                {
                    c.Item().Text("Declaration").Bold().Underline();
                    c.Item().Text($"Your account has been credited with the amount above against Invoice No. {_invoice.InvoiceNumber} " +
                                  $"dated {FormatDate(_invoice.InvoiceDate)}. The GST shown is reduced accordingly.");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(_invoice.SellerBankName) || !string.IsNullOrWhiteSpace(_invoice.SellerBankAccountNumber))
                {
                    c.Item().Text("Company's Bank Details").Bold();
                    BankLine(c, "Bank Name", _invoice.SellerBankName);
                    BankLine(c, "A/c No.", _invoice.SellerBankAccountNumber);
                    BankLine(c, "Branch & IFS Code", JoinNonEmpty(_invoice.SellerBankBranch, _invoice.SellerBankIfsc));
                    c.Item().Height(6);
                }

                c.Item().Text("Declaration").Bold().Underline();
                c.Item().Text(_invoice.DeclarationText);
            });

            row.RelativeItem().BorderLeft(Line).Padding(4).Column(c =>
            {
                c.Item().AlignRight().Text($"for {_invoice.SellerName}").Bold();
                c.Item().Height(42);
                c.Item().AlignRight().Text("Authorised Signatory");
            });
        });
    }

    private static void BankLine(ColumnDescriptor column, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        column.Item().Text(text =>
        {
            text.Span($"{label}: ");
            text.Span(value).Bold();
        });
    }

    // ---------------------------------------------------------------- helpers

    private static IEnumerable<string> Lines(string text) =>
        text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

    /// <example>1 Nov 2026 → "1-Nov-2026", as Tally prints it.</example>
    private static string FormatDate(DateOnly date) => date.ToString("d-MMM-yyyy", Invariant);

    private static string JoinNonEmpty(params string?[] parts) =>
        string.Join(" & ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
