using SareeBilling.Application.Invoices;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Domain.Common;
using SareeBilling.Domain.Gst;

namespace SareeBilling.Desktop.ViewModels;

/// <summary>
/// One row in the invoice item list. The entered values are fixed once added
/// (edit = load back into the entry row); the calculated columns are refreshed
/// whenever the invoice is recalculated.
/// </summary>
public sealed class InvoiceLineViewModel : ObservableObject
{
    private int _lineNumber;
    private string _taxableText = string.Empty;
    private string _taxText = string.Empty;
    private string _amountText = string.Empty;

    public InvoiceLineViewModel(InvoiceItemInput input)
    {
        Input = input;
    }

    public InvoiceItemInput Input { get; }

    public string Description => Input.Description;
    public string HsnCode => Input.HsnCode;
    public string QuantityText => $"{IndianFormat.Quantity(Input.Quantity)} {Input.Unit}";
    public string RateText => IndianFormat.Amount(Input.Rate);
    public string GstRateText => IndianFormat.Percent(Input.GstRate) + "%";

    public int LineNumber { get => _lineNumber; set => SetProperty(ref _lineNumber, value); }
    public string TaxableText { get => _taxableText; private set => SetProperty(ref _taxableText, value); }
    public string TaxText { get => _taxText; private set => SetProperty(ref _taxText, value); }
    public string AmountText { get => _amountText; private set => SetProperty(ref _amountText, value); }

    public GstLineInput ToGstInput() => new(Input.Quantity, Input.Rate, Input.GstRate);

    /// <param name="result">Null when no customer is selected yet (tax type unknown).</param>
    public void Apply(GstLineResult? result, decimal taxableAmount)
    {
        TaxableText = IndianFormat.Amount(taxableAmount);
        TaxText = result is null ? "—" : IndianFormat.Amount(result.TotalTax);
        AmountText = result is null ? "—" : IndianFormat.Amount(result.LineTotal);
    }
}
