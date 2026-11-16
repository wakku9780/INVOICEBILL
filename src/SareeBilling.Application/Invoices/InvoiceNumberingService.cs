using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Common;

namespace SareeBilling.Application.Invoices;

public interface IInvoiceNumberingService
{
    /// <summary>The number the next invoice will get. Numbers never restart.</summary>
    Task<int> GetNextNumberAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets where numbering continues, e.g. 162 to follow invoice 161 made in Tally.
    /// Can never go back to a number an invoice in this app has already used.
    /// </summary>
    /// <exception cref="ValidationException">When the number is not allowed.</exception>
    Task SetNextNumberAsync(int nextNumber, CancellationToken cancellationToken = default);
}

public sealed class InvoiceNumberingService : IInvoiceNumberingService
{
    public const int MaxNumber = 99_999_999;

    private readonly IInvoiceRepository _invoices;

    public InvoiceNumberingService(IInvoiceRepository invoices)
    {
        _invoices = invoices;
    }

    public async Task<int> GetNextNumberAsync(CancellationToken cancellationToken = default) =>
        await _invoices.GetLastNumberAsync(cancellationToken) + 1;

    public async Task SetNextNumberAsync(int nextNumber, CancellationToken cancellationToken = default)
    {
        if (nextNumber is < 1 or > MaxNumber)
        {
            throw new ValidationException([$"Next invoice number must be between 1 and {MaxNumber}."]);
        }

        var highestUsed = await _invoices.GetHighestUsedNumberAsync(cancellationToken);
        if (nextNumber <= highestUsed)
        {
            throw new ValidationException(
            [
                $"Invoice number {highestUsed} has already been used. " +
                $"The next number must be {highestUsed + 1} or higher, so that no number is repeated."
            ]);
        }

        await _invoices.SetLastNumberAsync(nextNumber - 1, cancellationToken);
    }
}
