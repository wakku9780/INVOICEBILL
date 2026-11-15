using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SareeBilling.Application.Abstractions;
using SareeBilling.Domain.Entities;

namespace SareeBilling.Infrastructure.Pdf;

internal sealed class QuestPdfInvoiceRenderer : IInvoicePdfRenderer
{
    static QuestPdfInvoiceRenderer()
    {
        // The business qualifies for the free Community licence (annual turnover under US$1M),
        // as confirmed by the owner. A Professional licence is required above that.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] RenderPdf(Invoice invoice, byte[]? logo)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return new InvoicePdfDocument(invoice, logo).GeneratePdf();
    }

    public IReadOnlyList<byte[]> RenderPageImages(Invoice invoice, byte[]? logo, int dpi)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 72);

        return new InvoicePdfDocument(invoice, logo)
            .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = dpi })
            .ToList();
    }
}
