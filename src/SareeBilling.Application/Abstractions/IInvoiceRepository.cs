using SareeBilling.Domain.Entities;

namespace SareeBilling.Application.Abstractions;

public interface IInvoiceRepository
{
    /// <summary>
    /// In one database transaction: reserves the next number for the invoice's financial year,
    /// assigns it, and saves the invoice with all its items. Either everything is saved or nothing.
    /// </summary>
    /// <param name="formatNumber">Builds the display number from the sequence, e.g. 161 → "JAS/2026-27/161".</param>
    Task<Invoice> AddWithNextNumberAsync(Invoice invoice, Func<int, string> formatNumber, CancellationToken cancellationToken = default);

    /// <summary>The last number issued (or reserved) in this financial year; 0 if none.</summary>
    Task<int> GetLastSequenceAsync(string financialYear, CancellationToken cancellationToken = default);

    /// <summary>The highest number actually used by a saved invoice in this financial year; 0 if none.</summary>
    Task<int> GetHighestUsedSequenceAsync(string financialYear, CancellationToken cancellationToken = default);

    Task SetLastSequenceAsync(string financialYear, int lastNumber, CancellationToken cancellationToken = default);

    Task<Invoice?> GetByIdAsync(int id, bool includeItems, CancellationToken cancellationToken = default);

    /// <summary>Newest first, at most <paramref name="maxResults"/> rows.</summary>
    Task<IReadOnlyList<Invoices.InvoiceSummary>> SearchAsync(
        Invoices.InvoiceSearchCriteria criteria, int maxResults, CancellationToken cancellationToken = default);

    Task MarkCancelledAsync(int id, string reason, DateTime cancelledAtUtc, CancellationToken cancellationToken = default);
}
