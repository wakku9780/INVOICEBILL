using SareeBilling.Domain.Common;
using SareeBilling.Domain.Enums;
using SareeBilling.Domain.Gst;

namespace SareeBilling.Application.CreditNotes;

/// <summary>
/// Credit note arithmetic and limits. Pure functions: used on screen while typing and again
/// inside the save transaction against the invoice as it is at that moment.
/// </summary>
public static class CreditNoteRules
{
    /// <summary>Invoice quantities are stored with 3 decimals.</summary>
    public const int QuantityDecimals = 3;

    /// <summary>
    /// The last day GST on an invoice can be reduced by a credit note: 30 November after the end of
    /// the invoice's financial year (e.g. invoices of 2026-27 → 30-Nov-2027).
    /// </summary>
    public static DateOnly GstDeadline(DateOnly invoiceDate)
    {
        var financialYearEnds = invoiceDate.Month >= 4 ? invoiceDate.Year + 1 : invoiceDate.Year;
        return new DateOnly(financialYearEnds, 11, 30);
    }

    public static bool IsPastGstDeadline(DateOnly invoiceDate, DateOnly creditNoteDate) =>
        creditNoteDate > GstDeadline(invoiceDate);

    /// <summary>
    /// Works out every line and the totals. Lines with nothing entered (0) are skipped. Problems are
    /// returned as messages; the calculation is null when there is any problem.
    /// </summary>
    public static (CreditNoteCalculation? Result, List<string> Errors) Calculate(
        CreditableInvoice invoice, CreditNoteKind kind, IReadOnlyList<CreditNoteLineRequest> requested)
    {
        var errors = new List<string>();
        var lines = invoice.Lines.ToDictionary(l => l.InvoiceItemId);
        var results = new List<CreditNoteLineResult>();

        if (!Enum.IsDefined(kind))
        {
            errors.Add("Choose what the credit note is for.");
            return (null, errors);
        }

        var chosen = requested
            .Where(r => kind == CreditNoteKind.GoodsReturn ? r.Quantity != 0 : r.TaxableAmount != 0)
            .ToList();

        foreach (var group in chosen.GroupBy(r => r.InvoiceItemId))
        {
            if (group.Count() > 1)
            {
                errors.Add("The same invoice line is listed twice.");
                continue;
            }

            var request = group.Single();
            if (!lines.TryGetValue(request.InvoiceItemId, out var line))
            {
                errors.Add("A selected line is not on this invoice. Please load the invoice again.");
                continue;
            }

            var label = $"Line {line.LineNumber} ({line.Description})";
            var result = kind == CreditNoteKind.GoodsReturn
                ? GoodsLine(invoice.TaxType, line, request.Quantity, label, errors)
                : AmountLine(invoice.TaxType, line, request.TaxableAmount, label, errors);
            if (result is not null)
            {
                results.Add(result);
            }
        }

        if (chosen.Count == 0)
        {
            errors.Add(kind == CreditNoteKind.GoodsReturn
                ? "Enter the quantity returned for at least one item."
                : "Enter the amount to reduce for at least one item.");
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        results = results.OrderBy(r => r.Source.LineNumber).ToList();
        var taxable = results.Sum(r => r.TaxableAmount);
        var cgst = results.Sum(r => r.CgstAmount);
        var sgst = results.Sum(r => r.SgstAmount);
        var igst = results.Sum(r => r.IgstAmount);
        var beforeRounding = taxable + cgst + sgst + igst;
        var grand = Money.RoundToRupee(beforeRounding);
        if (grand <= 0)
        {
            errors.Add("The credit note total comes to less than one rupee. Enter a larger quantity or amount.");
            return (null, errors);
        }

        return (new CreditNoteCalculation(
            results, results.Sum(r => r.Quantity), taxable, cgst, sgst, igst, cgst + sgst + igst,
            grand - beforeRounding, grand), errors);
    }

    private static CreditNoteLineResult? GoodsLine(TaxType taxType, CreditableLine line, decimal quantity, string label, List<string> errors)
    {
        if (quantity < 0)
        {
            errors.Add($"{label}: the quantity returned cannot be negative.");
            return null;
        }

        if (decimal.Round(quantity, QuantityDecimals) != quantity)
        {
            errors.Add($"{label}: use at most {QuantityDecimals} decimal places for the quantity.");
            return null;
        }

        if (quantity > line.RemainingQuantity)
        {
            errors.Add(line.RemainingQuantity <= 0
                ? $"{label}: everything on this line has already been returned."
                : $"{label}: only {IndianFormat.Quantity(line.RemainingQuantity)} {line.Unit} can still be returned " +
                  $"({IndianFormat.Quantity(line.Quantity)} billed, {IndianFormat.Quantity(line.ReturnedQuantity)} already returned).");
            return null;
        }

        decimal taxable;
        if (quantity == line.RemainingQuantity)
        {
            // Returning everything left: credit exactly what is left, so the paise always balance.
            taxable = line.RemainingTaxable;
        }
        else if (line.AmountOnlyCredited == 0)
        {
            taxable = Math.Min(Money.Round2(quantity * line.Rate), line.RemainingTaxable);
        }
        else
        {
            // The line's value was reduced earlier (discount): return at the reduced value per unit.
            taxable = Money.Round2(quantity * line.RemainingTaxable / line.RemainingQuantity);
        }

        if (taxable <= 0)
        {
            errors.Add($"{label}: nothing is left to credit on this line.");
            return null;
        }

        // Printed rate: the invoice rate, or the reduced rate per unit after an earlier discount.
        var rate = line.AmountOnlyCredited == 0 ? line.Rate : Money.Round2(taxable / quantity);
        return Line(taxType, line, quantity, rate, taxable);
    }

    private static CreditNoteLineResult? AmountLine(TaxType taxType, CreditableLine line, decimal amount, string label, List<string> errors)
    {
        if (amount < 0)
        {
            errors.Add($"{label}: the amount cannot be negative.");
            return null;
        }

        if (amount != Money.Round2(amount))
        {
            errors.Add($"{label}: use at most 2 decimal places.");
            return null;
        }

        if (amount > line.RemainingTaxable)
        {
            errors.Add(line.RemainingTaxable <= 0
                ? $"{label}: the full value of this line has already been credited."
                : $"{label}: at most {IndianFormat.Amount(line.RemainingTaxable)} (before GST) can still be reduced on this line.");
            return null;
        }

        return Line(taxType, line, 0m, 0m, amount);
    }

    private static CreditNoteLineResult Line(TaxType taxType, CreditableLine line, decimal quantity, decimal rate, decimal taxable)
    {
        var gst = GstCalculator.CalculateOnTaxable(taxable, line.GstRate, taxType);
        return new CreditNoteLineResult(line, quantity, rate, gst.TaxableAmount, gst.CgstAmount, gst.SgstAmount, gst.IgstAmount,
            gst.CgstRate, gst.SgstRate, gst.IgstRate);
    }
}
