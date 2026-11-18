using SareeBilling.Domain.Entities;

namespace SareeBilling.Application.Abstractions;

public interface IInvoiceRepository
{
    /// <summary>
    /// In one database transaction: reserves the next running number, assigns it
    /// (InvoiceNumber = "161"), and saves the invoice with all its items.
    /// Either everything is saved or nothing.
    /// </summary>
    Task<Invoice> AddWithNextNumberAsync(Invoice invoice, CancellationToken cancellationToken = default);

    /// <summary>The last number issued (or reserved by "next invoice number"); 0 if none.</summary>
    Task<int> GetLastNumberAsync(CancellationToken cancellationToken = default);

    /// <summary>The highest number actually used by a saved invoice; 0 if none.</summary>
    Task<int> GetHighestUsedNumberAsync(CancellationToken cancellationToken = default);

    Task SetLastNumberAsync(int lastNumber, CancellationToken cancellationToken = default);

    Task<Invoice?> GetByIdAsync(int id, bool includeItems, CancellationToken cancellationToken = default);

    /// <summary>Newest first, at most <paramref name="maxResults"/> rows.</summary>
    Task<IReadOnlyList<Invoices.InvoiceSummary>> SearchAsync(
        Invoices.InvoiceSearchCriteria criteria, int maxResults, CancellationToken cancellationToken = default);

    Task MarkCancelledAsync(int id, string reason, DateTime cancelledAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Every invoice dated <paramref name="from"/> to <paramref name="to"/> (inclusive), cancelled ones too.</summary>
    Task<IReadOnlyList<Dashboard.SalesRow>> ListSalesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
