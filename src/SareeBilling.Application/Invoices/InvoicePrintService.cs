using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Common;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Application.Invoices;

/// <summary>Everything needed to preview, print or save one invoice.</summary>
public sealed record InvoicePrintout(
    int InvoiceId,
    string InvoiceNumber,
    string BuyerName,
    bool IsCancelled,
    string SuggestedFileName,
    byte[] Pdf,
    IReadOnlyList<byte[]> PageImages);

public interface IInvoicePrintService
{
    /// <summary>Builds the PDF and page images for a saved invoice, exactly as stored.</summary>
    /// <exception cref="ValidationException">When the invoice does not exist.</exception>
    Task<InvoicePrintout> CreateAsync(int invoiceId, CancellationToken cancellationToken = default);
}

public sealed class InvoicePrintService : IInvoicePrintService
{
    /// <summary>300 dpi gives sharp text on paper; the preview scales it down.</summary>
    public const int PrintDpi = 300;

    private readonly IInvoiceRepository _invoices;
    private readonly ICompanyProfileRepository _companyProfiles;
    private readonly IInvoicePdfRenderer _renderer;

    public InvoicePrintService(
        IInvoiceRepository invoices,
        ICompanyProfileRepository companyProfiles,
        IInvoicePdfRenderer renderer)
    {
        _invoices = invoices;
        _companyProfiles = companyProfiles;
        _renderer = renderer;
    }

    public async Task<InvoicePrintout> CreateAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoices.GetByIdAsync(invoiceId, includeItems: true, cancellationToken)
                      ?? throw new ValidationException(["This invoice no longer exists."]);

        // The logo is not part of the invoice snapshot; the current company logo is used.
        var logo = (await _companyProfiles.GetAsync(cancellationToken))?.Logo;

        // Rendering is CPU work; keep it off the UI thread.
        var (pdf, images) = await Task.Run(
            () => (_renderer.RenderPdf(invoice, logo), _renderer.RenderPageImages(invoice, logo, PrintDpi)),
            cancellationToken);

        return new InvoicePrintout(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.BuyerName,
            invoice.Status == InvoiceStatus.Cancelled,
            SuggestFileName(invoice.InvoiceNumber),
            pdf,
            images);
    }

    /// <example>"JAS/2026-27/161" → "JAS-2026-27-161.pdf"</example>
    public static string SuggestFileName(string invoiceNumber)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(invoiceNumber.Select(c => c == '/' || invalid.Contains(c) ? '-' : c).ToArray());
        return safe + ".pdf";
    }
}
