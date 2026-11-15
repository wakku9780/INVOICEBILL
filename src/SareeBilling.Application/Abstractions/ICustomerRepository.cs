using SareeBilling.Domain.Entities;

namespace SareeBilling.Application.Abstractions;

public interface ICustomerRepository
{
    /// <summary>Matches name, GSTIN, city or phone (case-insensitive). Ordered by name.</summary>
    Task<IReadOnlyList<Customer>> SearchAsync(string? searchText, bool includeInactive, CancellationToken cancellationToken = default);

    Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> GstinInUseAsync(string gstin, int excludeCustomerId, CancellationToken cancellationToken = default);

    Task<bool> HasInvoicesAsync(int customerId, CancellationToken cancellationToken = default);

    /// <returns>The new customer's Id.</returns>
    Task<int> AddAsync(Customer customer, CancellationToken cancellationToken = default);

    Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default);

    Task DeleteAsync(int customerId, CancellationToken cancellationToken = default);
}
