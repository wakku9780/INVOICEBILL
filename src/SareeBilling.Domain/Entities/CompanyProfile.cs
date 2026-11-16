using SareeBilling.Domain.Common;

namespace SareeBilling.Domain.Entities;

/// <summary>
/// The seller's own details. There is exactly one row.
/// Invoices copy these values at save time, so editing the profile
/// never changes invoices that were already issued.
/// </summary>
public class CompanyProfile : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? Pincode { get; set; }
    public string State { get; set; } = string.Empty;

    /// <summary>Two-digit GST state code, e.g. "09" for Uttar Pradesh.</summary>
    public string StateCode { get; set; } = string.Empty;

    public string Gstin { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }

    /// <summary>
    /// Optional logo (PNG or JPEG bytes). Stored in the database so that
    /// backups and restores on another computer always include it.
    /// </summary>
    public byte[]? Logo { get; set; }

    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfsc { get; set; }
    public string? BankBranch { get; set; }

    public string DeclarationText { get; set; } =
        "We declare that this invoice shows the actual price of the goods described and that all particulars are true and correct.";
}
