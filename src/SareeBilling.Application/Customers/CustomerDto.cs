namespace SareeBilling.Application.Customers;

public sealed record CustomerDto
{
    /// <summary>0 for a new customer.</summary>
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;
    public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string? Pincode { get; init; }

    /// <summary>Set from StateCode when saving; only StateCode needs to be supplied.</summary>
    public string State { get; init; } = string.Empty;

    public string StateCode { get; init; } = string.Empty;
    public string? Gstin { get; init; }
    public string? Phone { get; init; }
    public bool IsActive { get; init; } = true;
}
