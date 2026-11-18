using System.Windows.Input;
using System.Windows.Media.Imaging;
using SareeBilling.Application.Receivables;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Services;

namespace SareeBilling.Desktop.ViewModels;

/// <summary>Preview window for any printable document (invoice, receipt, statement, report).</summary>
public sealed class InvoicePreviewViewModel : ObservableObject
{
    private const int PreviewDecodeWidth = 1400;

    public InvoicePreviewViewModel(DocumentPrintout document, IDocumentActions actions)
    {
        Document = document;
        Pages = document.PageImages.Select(p => InvoiceActions.LoadImage(p, PreviewDecodeWidth)).ToList();
        PrintCommand = new RelayCommand(() => actions.Print(document));
        SavePdfCommand = new RelayCommand(() => actions.SavePdf(document));
    }

    public DocumentPrintout Document { get; }

    public IReadOnlyList<BitmapImage> Pages { get; }

    public string WindowTitle => Document.Title;

    public string PageCountText => Pages.Count == 1 ? "1 page" : $"{Pages.Count} pages";

    public bool IsCancelled => Document.IsCancelled;

    public ICommand PrintCommand { get; }

    public ICommand SavePdfCommand { get; }
}
