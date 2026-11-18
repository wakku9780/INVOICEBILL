using SareeBilling.Application.Receivables;
using SareeBilling.Domain.Entities;

namespace SareeBilling.Application.Abstractions;

public interface IReceivablesRepository
{
    /// <summary>The customer's bills and credits as saved now, or null when the customer does not exist.</summary>
    Task<CustomerAccount?> GetAccountAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Accounts of every customer that has any bill or credit.</summary>
    Task<IReadOnlyList<CustomerAccount>> GetAllAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a receipt with the next R- number in one write transaction. <paramref name="checkAndPlan"/>
    /// receives the customer's account as it is inside that transaction and returns the bills to pay
    /// (or throws ValidationException); nothing is saved if it throws.
    /// </summary>
    Task<Receipt> AddReceiptAsync(
        Receipt receipt,
        Func<CustomerAccount, IReadOnlyList<AllocationLine>> checkAndPlan,
        CancellationToken cancellationToken = default);

    /// <summary>Matches existing advances to bills in one write transaction (see <see cref="AddReceiptAsync"/>).</summary>
    Task AddAllocationsAsync(
        int customerId,
        DateOnly allocatedOn,
        Func<CustomerAccount, IReadOnlyList<PlannedAllocation>> checkAndPlan,
        CancellationToken cancellationToken = default);

    /// <summary>Marks the receipt cancelled and removes its matches, so its bills are pending again.</summary>
    Task CancelReceiptAsync(int receiptId, string reason, DateTime cancelledAtUtc, CancellationToken cancellationToken = default);

    Task<ReceiptDetail?> GetReceiptAsync(int receiptId, CancellationToken cancellationToken = default);

    /// <summary>Newest first, at most <paramref name="maxResults"/> rows.</summary>
    Task<IReadOnlyList<ReceiptSummary>> SearchReceiptsAsync(ReceiptSearchCriteria criteria, int maxResults, CancellationToken cancellationToken = default);

    /// <summary>Receipts (not cancelled) dated <paramref name="from"/> to <paramref name="to"/>.</summary>
    Task<IReadOnlyList<ReceiptRow>> ListReceiptsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OpeningBillDto>> GetOpeningBillsAsync(int customerId, CancellationToken cancellationToken = default);

    Task<int> AddOpeningBillAsync(OpeningBill bill, CancellationToken cancellationToken = default);

    /// <summary>Removes an opening entry. Returns false (and removes nothing) when money is already matched to it.</summary>
    Task<bool> RemoveOpeningBillAsync(int openingBillId, CancellationToken cancellationToken = default);

    /// <summary>Amount received against an invoice (matched from receipts or advances).</summary>
    Task<decimal> GetSettledAmountAsync(int invoiceId, CancellationToken cancellationToken = default);
}
