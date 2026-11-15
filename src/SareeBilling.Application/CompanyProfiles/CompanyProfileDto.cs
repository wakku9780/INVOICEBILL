namespace SareeBilling.Application.CompanyProfiles;

public sealed record CompanyProfileDto
{
    public string Name { get; init; } = string.Empty;
    public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string? Pincode { get; init; }

    /// <summary>Set from StateCode when saving; only StateCode needs to be supplied.</summary>
    public string State { get; init; } = string.Empty;

    public string StateCode { get; init; } = string.Empty;
    public string Gstin { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public byte[]? Logo { get; init; }
    public string InvoicePrefix { get; init; } = string.Empty;
    public string? BankName { get; init; }
    public string? BankAccountNumber { get; init; }
    public string? BankIfsc { get; init; }
    public string? BankBranch { get; init; }
    public string DeclarationText { get; init; } = string.Empty;
}
