namespace SareeBilling.Domain.Entities;

/// <summary>
/// The single running invoice counter (exactly one row). Numbers never restart.
/// The next invoice gets LastNumber + 1 (or the highest saved number + 1 if that is larger).
/// Setting LastNumber lets numbering continue after the last invoice made in Tally.
/// </summary>
public class InvoiceCounter
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public int LastNumber { get; set; }
}
