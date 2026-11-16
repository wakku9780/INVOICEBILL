using System.Windows.Input;
using System.Windows.Media.Imaging;
using SareeBilling.Application.Invoices;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Services;

namespace SareeBilling.Desktop.ViewModels;

/// <summary>Shows the invoice exactly as it will print, with Print and Save as PDF.</summary>
public sealed class InvoicePreviewViewModel : ObservableObject
{
    private const int PreviewDecodeWidth = 1400;

    public InvoicePreviewViewModel(InvoicePrintout printout, IInvoiceActions actions)
    {
        Printout = printout;
        Pages = printout.PageImages.Select(p => InvoiceActions.LoadImage(p, PreviewDecodeWidth)).ToList();
        PrintCommand = new RelayCommand(() => actions.Print(printout));
        SavePdfCommand = new RelayCommand(() => actions.SavePdf(printout));
    }

    public InvoicePrintout Printout { get; }

    public IReadOnlyList<BitmapImage> Pages { get; }

    public string WindowTitle => $"Invoice {Printout.InvoiceNumber} - {Printout.BuyerName}";

    public string PageCountText => Pages.Count == 1 ? "1 page" : $"{Pages.Count} pages";

    public bool IsCancelled => Printout.IsCancelled;

    public ICommand PrintCommand { get; }

    public ICommand SavePdfCommand { get; }
}
