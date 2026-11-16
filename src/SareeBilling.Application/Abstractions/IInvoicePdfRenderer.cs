using SareeBilling.Domain.Entities;

namespace SareeBilling.Application.Abstractions;

/// <summary>Turns a saved invoice into an A4 PDF and into page images (for preview and printing).</summary>
public interface IInvoicePdfRenderer
{
    byte[] RenderPdf(Invoice invoice, byte[]? logo);

    /// <returns>One PNG per page.</returns>
    IReadOnlyList<byte[]> RenderPageImages(Invoice invoice, byte[]? logo, int dpi);
}
