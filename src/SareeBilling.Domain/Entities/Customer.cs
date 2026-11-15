using SareeBilling.Domain.Common;

namespace SareeBilling.Domain.Entities;

public class Customer : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? Pincode { get; set; }
    public string State { get; set; } = string.Empty;

    /// <summary>Two-digit GST state code. Always required: it decides CGST/SGST vs IGST.</summary>
    public string StateCode { get; set; } = string.Empty;

    /// <summary>Optional: unregistered customers have no GSTIN.</summary>
    public string? Gstin { get; set; }

    public string? Phone { get; set; }

    /// <summary>
    /// Customers that already have invoices are deactivated instead of deleted,
    /// so invoice history stays intact.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}
