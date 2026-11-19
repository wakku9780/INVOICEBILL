using SareeBilling.Application.CreditNotes;
using SareeBilling.Domain.Entities;

namespace SareeBilling.Application.Abstractions;

public interface ICreditNoteRepository
{
    /// <summary>The invoice with what is already credited on each line, or null when it does not exist.</summary>
    Task<CreditableInvoice?> GetCreditableInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Finds a saved invoice by its printed number (e.g. "161").</summary>
    Task<int?> FindInvoiceIdAsync(string invoiceNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// In one write transaction: <paramref name="build"/> receives the invoice as it is now and returns the
    /// credit note to save (or throws ValidationException; then nothing is saved). The credit note gets
    /// the next CN- number, and as much of it as is pending on the invoice is matched to the invoice;
    /// the rest stays as the customer's advance.
    /// </summary>
    /// <returns>The saved credit note and the amount matched to the invoice.</returns>
    Task<(CreditNote CreditNote, decimal AppliedToInvoice)> AddAsync(
        int invoiceId,
        Func<CreditableInvoice, CreditNote> build,
        CancellationToken cancellationToken = default);

    /// <summary>Marks it cancelled and removes every match made from it, so those bills are pending again.</summary>
    Task CancelAsync(int creditNoteId, string reason, DateTime cancelledAtUtc, CancellationToken cancellationToken = default);

    Task<CreditNote?> GetAsync(int creditNoteId, CancellationToken cancellationToken = default);

    /// <summary>Newest first, at most <paramref name="maxResults"/> rows.</summary>
    Task<IReadOnlyList<CreditNoteSummary>> SearchAsync(CreditNoteSearchCriteria criteria, int maxResults, CancellationToken cancellationToken = default);

    /// <summary>Credit notes (not cancelled) dated <paramref name="from"/> to <paramref name="to"/>.</summary>
    Task<IReadOnlyList<CreditNoteRow>> ListAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Numbers of the credit notes (not cancelled) issued against an invoice.</summary>
    Task<IReadOnlyList<string>> GetActiveNumbersForInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default);
}
