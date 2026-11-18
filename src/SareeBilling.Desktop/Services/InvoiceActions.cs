using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SareeBilling.Application.Common;
using SareeBilling.Application.Invoices;
using SareeBilling.Application.Receivables;
using SareeBilling.Domain.Common;

namespace SareeBilling.Desktop.Services;

/// <summary>Invoice actions shared by the invoice screens: preview, print, save as PDF, cancel.</summary>
public interface IInvoiceActions
{
    Task PreviewAsync(int invoiceId);

    Task PrintAsync(int invoiceId);

    Task SavePdfAsync(int invoiceId);

    /// <summary>Asks for a reason and cancels the invoice. Returns true when it was cancelled.</summary>
    Task<bool> CancelAsync(int invoiceId, string invoiceNumber);

    void Print(InvoicePrintout printout);

    void SavePdf(InvoicePrintout printout);
}

public sealed class InvoiceActions : IInvoiceActions
{
    private readonly IInvoicePrintService _printing;
    private readonly IInvoiceService _invoices;
    private readonly IReceiptService _receipts;
    private readonly IDocumentActions _documents;
    private readonly IDialogService _dialogs;

    public InvoiceActions(
        IInvoicePrintService printing,
        IInvoiceService invoices,
        IReceiptService receipts,
        IDocumentActions documents,
        IDialogService dialogs)
    {
        _printing = printing;
        _invoices = invoices;
        _receipts = receipts;
        _documents = documents;
        _dialogs = dialogs;
    }

    public async Task PreviewAsync(int invoiceId) => _documents.Preview(ToDocument(await CreatePrintoutAsync(invoiceId)));

    public async Task PrintAsync(int invoiceId) => Print(await CreatePrintoutAsync(invoiceId));

    public async Task SavePdfAsync(int invoiceId) => SavePdf(await CreatePrintoutAsync(invoiceId));

    public async Task<bool> CancelAsync(int invoiceId, string invoiceNumber)
    {
        // Money already received against the invoice is not lost: it becomes the customer's advance.
        var received = await _receipts.GetSettledAmountForInvoiceAsync(invoiceId);
        var paymentNote = received > 0
            ? $"\n\n₹ {IndianFormat.Amount(received)} has already been received against this invoice. " +
              "After cancelling, that money stays with the customer as an advance, ready to adjust against another invoice."
            : string.Empty;

        var reason = _dialogs.PromptForText(
            "Cancel invoice",
            $"Why is invoice {invoiceNumber} being cancelled?\n\n" +
            "It will stay in Invoice History marked CANCELLED, and its number will not be used again." + paymentNote,
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

    public void Print(InvoicePrintout printout) => _documents.Print(ToDocument(printout));

    public void SavePdf(InvoicePrintout printout) => _documents.SavePdf(ToDocument(printout));

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

    private static DocumentPrintout ToDocument(InvoicePrintout p) =>
        new($"Invoice {p.InvoiceNumber} - {p.BuyerName}", p.IsCancelled, p.SuggestedFileName, p.Pdf, p.PageImages);

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
}
