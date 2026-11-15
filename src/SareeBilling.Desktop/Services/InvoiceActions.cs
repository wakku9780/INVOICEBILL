using System.Diagnostics;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SareeBilling.Application.Common;
using SareeBilling.Application.Invoices;
using SareeBilling.Desktop.ViewModels;
using SareeBilling.Desktop.Views;

namespace SareeBilling.Desktop.Services;

/// <summary>
/// The things staff do with a saved invoice — preview, print, save as PDF, cancel —
/// in one place, so the Create Invoice and Invoice History screens behave identically.
/// </summary>
public interface IInvoiceActions
{
    Task PreviewAsync(int invoiceId);

    Task PrintAsync(int invoiceId);

    Task SavePdfAsync(int invoiceId);

    /// <returns>True if the invoice was cancelled.</returns>
    Task<bool> CancelAsync(int invoiceId, string invoiceNumber);

    void Print(InvoicePrintout printout);

    void SavePdf(InvoicePrintout printout);
}

public sealed class InvoiceActions : IInvoiceActions
{
    /// <summary>A4 in WPF units (1/96 inch).</summary>
    private static readonly Size A4 = new(8.27 * 96, 11.69 * 96);

    private readonly IInvoicePrintService _printing;
    private readonly IInvoiceService _invoices;
    private readonly IDialogService _dialogs;
    private string? _lastSaveFolder;

    public InvoiceActions(IInvoicePrintService printing, IInvoiceService invoices, IDialogService dialogs)
    {
        _printing = printing;
        _invoices = invoices;
        _dialogs = dialogs;
    }

    public async Task PreviewAsync(int invoiceId)
    {
        var printout = await CreatePrintoutAsync(invoiceId);
        var window = new InvoicePreviewWindow(new InvoicePreviewViewModel(printout, this))
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    public async Task PrintAsync(int invoiceId) => Print(await CreatePrintoutAsync(invoiceId));

    public async Task SavePdfAsync(int invoiceId) => SavePdf(await CreatePrintoutAsync(invoiceId));

    public async Task<bool> CancelAsync(int invoiceId, string invoiceNumber)
    {
        var reason = _dialogs.PromptForText(
            "Cancel invoice",
            $"Why is invoice {invoiceNumber} being cancelled?\n\n" +
            "It will stay in Invoice History marked CANCELLED, and its number will not be used again.",
            "Cancel invoice");

        if (reason is null)
        {
            return false;
        }

        try
        {
            await _invoices.CancelAsync(invoiceId, reason);
            return true;
        }
        catch (ValidationException ex)
        {
            _dialogs.ShowError(string.Join("\n", ex.Errors));
            return false;
        }
    }

    public void Print(InvoicePrintout printout)
    {
        var dialog = new PrintDialog { UserPageRangeEnabled = false };
        dialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
        dialog.PrintTicket.PageOrientation = PageOrientation.Portrait;

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = A4;

        foreach (var png in printout.PageImages)
        {
            var page = new FixedPage { Width = A4.Width, Height = A4.Height };
            page.Children.Add(new System.Windows.Controls.Image
            {
                Source = LoadImage(png, decodeWidth: null),
                Width = A4.Width,
                Height = A4.Height
            });

            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            document.Pages.Add(content);
        }

        dialog.PrintDocument(document.DocumentPaginator, $"Invoice {printout.InvoiceNumber}");
    }

    public void SavePdf(InvoicePrintout printout)
    {
        var folder = _lastSaveFolder ?? DefaultSaveFolder();
        var dialog = new SaveFileDialog
        {
            Title = "Save invoice as PDF",
            Filter = "PDF document (*.pdf)|*.pdf",
            FileName = printout.SuggestedFileName,
            InitialDirectory = folder,
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) != true)
        {
            return;
        }

        File.WriteAllBytes(dialog.FileName, printout.Pdf);
        _lastSaveFolder = Path.GetDirectoryName(dialog.FileName);

        if (_dialogs.Confirm($"Saved:\n{dialog.FileName}\n\nOpen the PDF now?", "PDF saved"))
        {
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
    }

    /// <summary>Decodes a page image. Preview uses a smaller decode width to save memory.</summary>
    public static BitmapImage LoadImage(byte[] png, int? decodeWidth)
    {
        var image = new BitmapImage();
        using (var stream = new MemoryStream(png))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth is { } width)
            {
                image.DecodePixelWidth = width;
            }

            image.StreamSource = stream;
            image.EndInit();
        }

        image.Freeze();
        return image;
    }

    private async Task<InvoicePrintout> CreatePrintoutAsync(int invoiceId)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            return await _printing.CreateAsync(invoiceId);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private static string DefaultSaveFolder()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SareeBilling Invoices");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
