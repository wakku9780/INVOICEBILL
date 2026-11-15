namespace SareeBilling.Domain.Common;

/// <summary>
/// Base class for persisted entities. Timestamps are stored in UTC and
/// are set automatically by the persistence layer on save.
/// </summary>
public abstract class AuditableEntity
{
    public int Id { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
}
