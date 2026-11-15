using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Common;
using SareeBilling.Domain.Entities;
using SareeBilling.Domain.Gst;

namespace SareeBilling.Application.Customers;

public enum CustomerRemovalResult
{
    /// <summary>The customer had no invoices and was removed permanently.</summary>
    Deleted,

    /// <summary>The customer has invoices, so it was hidden instead of deleted.</summary>
    Deactivated
}

public interface ICustomerService
{
    Task<IReadOnlyList<CustomerDto>> SearchAsync(string? searchText, bool includeInactive, CancellationToken cancellationToken = default);

    Task<CustomerDto?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Adds (Id = 0) or updates a customer.</summary>
    /// <returns>The customer's Id.</returns>
    /// <exception cref="ValidationException">When any field is invalid.</exception>
    Task<int> SaveAsync(CustomerDto customer, CancellationToken cancellationToken = default);

    Task<bool> HasInvoicesAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Deletes the customer, or deactivates it if it already has invoices.</summary>
    Task<CustomerRemovalResult> RemoveAsync(int customerId, CancellationToken cancellationToken = default);
}

public sealed class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _repository;

    public CustomerService(ICustomerRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<CustomerDto>> SearchAsync(
        string? searchText, bool includeInactive, CancellationToken cancellationToken = default)
    {
        var customers = await _repository.SearchAsync(InputRules.Clean(searchText), includeInactive, cancellationToken);
        return customers.Select(ToDto).ToList();
    }

    public async Task<CustomerDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var customer = await _repository.GetByIdAsync(id, cancellationToken);
        return customer is null ? null : ToDto(customer);
    }

    public async Task<int> SaveAsync(CustomerDto customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        var entity = Normalize(customer);
        var errors = Validate(entity);

        if (entity.Gstin is not null && errors.Count == 0 &&
            await _repository.GstinInUseAsync(entity.Gstin, entity.Id, cancellationToken))
        {
            errors.Add($"Another customer already has GSTIN {entity.Gstin}. Please search for it instead of adding it again.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        if (entity.Id == 0)
        {
            return await _repository.AddAsync(entity, cancellationToken);
        }

        await _repository.UpdateAsync(entity, cancellationToken);
        return entity.Id;
    }

    public Task<bool> HasInvoicesAsync(int customerId, CancellationToken cancellationToken = default) =>
        _repository.HasInvoicesAsync(customerId, cancellationToken);

    public async Task<CustomerRemovalResult> RemoveAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var customer = await _repository.GetByIdAsync(customerId, cancellationToken)
                       ?? throw new InvalidOperationException("This customer no longer exists.");

        if (await _repository.HasInvoicesAsync(customerId, cancellationToken))
        {
            customer.IsActive = false;
            await _repository.UpdateAsync(customer, cancellationToken);
            return CustomerRemovalResult.Deactivated;
        }

        await _repository.DeleteAsync(customerId, cancellationToken);
        return CustomerRemovalResult.Deleted;
    }

    private static Customer Normalize(CustomerDto dto)
    {
        var state = IndianStates.FindByCode(dto.StateCode?.Trim());
        var gstin = GstinValidator.Normalize(dto.Gstin);

        return new Customer
        {
            Id = dto.Id,
            Name = InputRules.CleanRequired(dto.Name),
            AddressLine1 = InputRules.CleanRequired(dto.AddressLine1),
            AddressLine2 = InputRules.Clean(dto.AddressLine2),
            City = InputRules.CleanRequired(dto.City),
            Pincode = InputRules.Clean(dto.Pincode),
            StateCode = state?.Code ?? string.Empty,
            State = state?.Name ?? string.Empty,
            Gstin = gstin.Length == 0 ? null : gstin,
            Phone = InputRules.Clean(dto.Phone),
            IsActive = dto.IsActive
        };
    }

    private static List<string> Validate(Customer c)
    {
        var errors = new List<string>();

        InputRules.Required(errors, c.Name, "Customer name", 200);
        InputRules.Required(errors, c.AddressLine1, "Address line 1", 250);
        InputRules.MaxLength(errors, c.AddressLine2, "Address line 2", 250);
        InputRules.Required(errors, c.City, "City", 100);
        InputRules.Pincode(errors, c.Pincode);
        var state = InputRules.State(errors, c.StateCode);
        InputRules.Gstin(errors, c.Gstin, state);
        InputRules.MaxLength(errors, c.Phone, "Phone", 60);

        return errors;
    }

    private static CustomerDto ToDto(Customer c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        AddressLine1 = c.AddressLine1,
        AddressLine2 = c.AddressLine2,
        City = c.City,
        Pincode = c.Pincode,
        State = c.State,
        StateCode = c.StateCode,
        Gstin = c.Gstin,
        Phone = c.Phone,
        IsActive = c.IsActive
    };
}
