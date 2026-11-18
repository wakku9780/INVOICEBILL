using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Common;
using SareeBilling.Domain.Common;
using SareeBilling.Domain.Entities;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Application.Receivables;

/// <summary>Old bills and advances from before this app was used (bill-wise opening balance).</summary>
public interface IOpeningBillService
{
    Task<IReadOnlyList<OpeningBillDto>> GetAsync(int customerId, CancellationToken cancellationToken = default);

    /// <exception cref="ValidationException">When a field is invalid.</exception>
    Task<int> AddAsync(OpeningBillDto bill, CancellationToken cancellationToken = default);

    /// <exception cref="ValidationException">When money has already been matched to the entry.</exception>
    Task RemoveAsync(int openingBillId, CancellationToken cancellationToken = default);
}

public sealed class OpeningBillService(IReceivablesRepository repository, ICustomerRepository customers, TimeProvider timeProvider)
    : IOpeningBillService
{
    private const int MaxBillNumberLength = 40;

    public Task<IReadOnlyList<OpeningBillDto>> GetAsync(int customerId, CancellationToken cancellationToken = default) =>
        repository.GetOpeningBillsAsync(customerId, cancellationToken);

    public async Task<int> AddAsync(OpeningBillDto bill, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bill);

        var customer = bill.CustomerId > 0 ? await customers.GetByIdAsync(bill.CustomerId, cancellationToken) : null;
        var errors = new List<string>();
        if (customer is null)
        {
            errors.Add("Save the customer first, then add old bills.");
        }

        var number = InputRules.CleanRequired(bill.BillNumber);
        var what = bill.Side == BalanceSide.Credit ? "Reference" : "Old bill number";
        InputRules.Required(errors, number, what, MaxBillNumberLength);

        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        if (bill.BillDate == default)
        {
            errors.Add("Enter the date of the old bill.");
        }
        else if (bill.BillDate > today)
        {
            errors.Add("The date of an old bill cannot be in the future.");
        }

        if (!Enum.IsDefined(bill.Side))
        {
            errors.Add("Choose whether the customer owes this amount or paid it in advance.");
        }

        if (bill.Amount <= 0)
        {
            errors.Add("Enter the amount still pending.");
        }
        else if (bill.Amount != Money.Round2(bill.Amount))
        {
            errors.Add("Amount: use at most 2 decimal places.");
        }
        else if (bill.Amount > ReceiptService.MaxAmount)
        {
            errors.Add("Amount is too large.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return await repository.AddOpeningBillAsync(new OpeningBill
        {
            CustomerId = customer!.Id,
            BillNumber = number,
            BillDate = bill.BillDate,
            DueDate = bill.BillDate.AddDays(customer.CreditDays),
            Side = bill.Side,
            Amount = bill.Amount
        }, cancellationToken);
    }

    public async Task RemoveAsync(int openingBillId, CancellationToken cancellationToken = default)
    {
        if (!await repository.RemoveOpeningBillAsync(openingBillId, cancellationToken))
        {
            throw new ValidationException(["Money is already matched to this entry. Cancel that receipt (or adjustment) first."]);
        }
    }
}
