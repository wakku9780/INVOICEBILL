using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Common;
using SareeBilling.Domain.Common;
using SareeBilling.Domain.Entities;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Application.Receivables;

public interface IReceiptService
{
    Task<CustomerAccount?> GetAccountAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Saves a receipt and its bill matches. Money not matched stays as the customer's advance.</summary>
    /// <exception cref="ValidationException">When anything is invalid; nothing is saved.</exception>
    Task<CreatedReceipt> CreateAsync(CreateReceiptRequest request, CancellationToken cancellationToken = default);

    /// <summary>Uses the customer's advance (unmatched receipts and opening advances) to pay bills, oldest advance first.</summary>
    Task AdjustAdvanceAsync(int customerId, IReadOnlyList<AllocationLine> lines, CancellationToken cancellationToken = default);

    Task CancelAsync(int receiptId, string reason, CancellationToken cancellationToken = default);

    Task<ReceiptDetail?> GetAsync(int receiptId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReceiptSummary>> SearchAsync(ReceiptSearchCriteria criteria, CancellationToken cancellationToken = default);

    /// <summary>Amount already received against an invoice (asked before the invoice is cancelled).</summary>
    Task<decimal> GetSettledAmountForInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Current balance (+ = Dr) of every customer that has any bill or receipt.</summary>
    Task<IReadOnlyDictionary<int, decimal>> GetBalancesAsync(CancellationToken cancellationToken = default);
}

public sealed class ReceiptService(IReceivablesRepository repository, ICustomerRepository customers, TimeProvider timeProvider)
    : IReceiptService
{
    public const int MaxSearchResults = 1000;
    public const decimal MaxAmount = 99_99_99_999.99m;
    private const int MaxReferenceLength = 100;
    private const int MaxLongTextLength = 1000;

    public Task<CustomerAccount?> GetAccountAsync(int customerId, CancellationToken cancellationToken = default) =>
        repository.GetAccountAsync(customerId, cancellationToken);

    public async Task<CreatedReceipt> CreateAsync(CreateReceiptRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var customer = request.CustomerId > 0 ? await customers.GetByIdAsync(request.CustomerId, cancellationToken) : null;
        var errors = new List<string>();
        if (customer is null)
        {
            errors.Add("Please select the customer who paid.");
        }

        CheckDate(errors, request.ReceiptDate, "Receipt date");

        if (request.Amount <= 0)
        {
            errors.Add("Enter the amount received.");
        }
        else if (request.Amount != Money.Round2(request.Amount))
        {
            errors.Add("Amount received: use at most 2 decimal places.");
        }
        else if (request.Amount > MaxAmount)
        {
            errors.Add("Amount received is too large.");
        }

        if (!Enum.IsDefined(request.Mode))
        {
            errors.Add("Choose how the money was paid.");
        }

        var reference = InputRules.Clean(request.Reference);
        var narration = InputRules.Clean(request.Narration);
        InputRules.MaxLength(errors, reference, "UTR / cheque no.", MaxReferenceLength);
        InputRules.MaxLength(errors, narration, "Narration", MaxLongTextLength);
        if (request.Mode == PaymentMode.Cheque && reference is null)
        {
            errors.Add("Enter the cheque number.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var receipt = new Receipt
        {
            ReceiptDate = request.ReceiptDate,
            CustomerId = customer!.Id,
            CustomerName = customer.Name,
            Amount = request.Amount,
            Mode = request.Mode,
            Reference = reference,
            Narration = narration,
            Status = VoucherStatus.Posted
        };

        var lines = request.Allocations.Where(l => l.Amount != 0).ToList();
        var saved = await repository.AddReceiptAsync(receipt, account =>
        {
            // Checked again inside the write transaction, against the bills as they are now.
            var problems = AllocationRules.Check(account, lines, request.Amount);
            if (problems.Count > 0)
            {
                throw new ValidationException(problems);
            }

            return lines;
        }, cancellationToken);

        return new CreatedReceipt(saved.Id, saved.ReceiptNumber, saved.Amount, saved.Amount - lines.Sum(l => l.Amount));
    }

    public async Task AdjustAdvanceAsync(int customerId, IReadOnlyList<AllocationLine> lines, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var chosen = lines.Where(l => l.Amount != 0).ToList();
        if (chosen.Count == 0)
        {
            throw new ValidationException(["Enter an amount against at least one bill."]);
        }

        await repository.AddAllocationsAsync(customerId, Today, account =>
        {
            var problems = AllocationRules.Check(account, chosen, account.Advance);
            if (problems.Count > 0)
            {
                throw new ValidationException(problems);
            }

            return AllocationRules.Distribute(account, chosen);
        }, cancellationToken);
    }

    public async Task CancelAsync(int receiptId, string reason, CancellationToken cancellationToken = default)
    {
        var cleanReason = reason?.Trim() ?? string.Empty;
        var errors = new List<string>();
        InputRules.Required(errors, cleanReason, "Reason for cancelling", MaxLongTextLength);

        var receipt = await repository.GetReceiptAsync(receiptId, cancellationToken)
                      ?? throw new ValidationException(["This receipt no longer exists."]);
        if (receipt.Status == VoucherStatus.Cancelled)
        {
            errors.Add($"Receipt {receipt.ReceiptNumber} is already cancelled.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        await repository.CancelReceiptAsync(receiptId, cleanReason, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }

    public Task<ReceiptDetail?> GetAsync(int receiptId, CancellationToken cancellationToken = default) =>
        repository.GetReceiptAsync(receiptId, cancellationToken);

    public Task<IReadOnlyList<ReceiptSummary>> SearchAsync(ReceiptSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        return repository.SearchReceiptsAsync(criteria with { Text = InputRules.Clean(criteria.Text) }, MaxSearchResults, cancellationToken);
    }

    public Task<decimal> GetSettledAmountForInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default) =>
        repository.GetSettledAmountAsync(invoiceId, cancellationToken);

    public async Task<IReadOnlyDictionary<int, decimal>> GetBalancesAsync(CancellationToken cancellationToken = default) =>
        (await repository.GetAllAccountsAsync(cancellationToken)).ToDictionary(a => a.CustomerId, a => a.Balance);

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    private void CheckDate(List<string> errors, DateOnly date, string field)
    {
        if (date == default)
        {
            errors.Add($"Enter the {field.ToLowerInvariant()}.");
        }
        else if (date > Today)
        {
            errors.Add($"{field} cannot be in the future.");
        }
    }
}
