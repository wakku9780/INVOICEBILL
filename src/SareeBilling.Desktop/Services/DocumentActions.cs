using System.Diagnostics;
using System.IO;
using System.Printing;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using Microsoft.Win32;
using SareeBilling.Application.Receivables;
using SareeBilling.Desktop.ViewModels;
using SareeBilling.Desktop.Views;

namespace SareeBilling.Desktop.Services;

/// <summary>Preview, print and save for every printable document (invoices, receipts, statements, reports).</summary>
public interface IDocumentActions
{
    void Preview(DocumentPrintout document);

    void Print(DocumentPrintout document);

    void SavePdf(DocumentPrintout document);

    /// <summary>Saves CSV text (opens in Excel) after asking where.</summary>
    void SaveCsv(string csv, string suggestedFileName);
}

public sealed class DocumentActions(IDialogService dialogs) : IDocumentActions
{
    /// <summary>A4 in WPF units (1/96 inch).</summary>
    private static readonly Size A4 = new(8.27 * 96, 11.69 * 96);

    private string? _lastPdfFolder;
    private string? _lastCsvFolder;

    public void Preview(DocumentPrintout document)
    {
        var window = new InvoicePreviewWindow(new InvoicePreviewViewModel(document, this))
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    public void Print(DocumentPrintout document)
    {
        var dialog = new PrintDialog { UserPageRangeEnabled = false };
        dialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
        dialog.PrintTicket.PageOrientation = PageOrientation.Portrait;

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var fixedDocument = new FixedDocument();
        fixedDocument.DocumentPaginator.PageSize = A4;

        foreach (var png in document.PageImages)
        {
            var page = new FixedPage { Width = A4.Width, Height = A4.Height };
            page.Children.Add(new System.Windows.Controls.Image
            {
                Source = InvoiceActions.LoadImage(png, decodeWidth: null),
                Width = A4.Width,
                Height = A4.Height
            });

            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            fixedDocument.Pages.Add(content);
        }

        dialog.PrintDocument(fixedDocument.DocumentPaginator, document.Title);
    }

    public void SavePdf(DocumentPrintout document)
    {
        var path = AskWhere("Save as PDF", "PDF document (*.pdf)|*.pdf", document.SuggestedFileName,
            _lastPdfFolder ?? DefaultFolder("SareeBilling Invoices"));
        if (path is null)
        {
            return;
        }

        File.WriteAllBytes(path, document.Pdf);
        _lastPdfFolder = Path.GetDirectoryName(path);
        OfferToOpen(path, "PDF saved");
    }

    public void SaveCsv(string csv, string suggestedFileName)
    {
        var path = AskWhere("Export for Excel", "CSV file for Excel (*.csv)|*.csv", suggestedFileName,
            _lastCsvFolder ?? DefaultFolder("SareeBilling Reports"));
        if (path is null)
        {
            return;
        }

        // UTF-8 with BOM so Excel shows ₹ and Indian names correctly.
        File.WriteAllText(path, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        _lastCsvFolder = Path.GetDirectoryName(path);
        OfferToOpen(path, "File saved");
    }

    private static string? AskWhere(string title, string filter, string fileName, string folder)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = fileName,
            InitialDirectory = folder,
            AddExtension = true,
            OverwritePrompt = true
        };

        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    private void OfferToOpen(string path, string title)
    {
        if (dialogs.Confirm($"Saved:\n{path}\n\nOpen it now?", title))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    private static string DefaultFolder(string name)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), name);
        Directory.CreateDirectory(folder);
        return folder;
    }
}
