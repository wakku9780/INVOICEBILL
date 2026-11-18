using SareeBilling.Domain.Common;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Domain.Entities;

/// <summary>
/// A customer's balance from before this app was used, entered bill by bill (as in Tally):
/// an old unpaid bill (Debit) or an old advance (Credit).
/// </summary>
public class OpeningBill : AuditableEntity
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>The old bill's number, e.g. from Tally ("145"), or a short note for an advance.</summary>
    public string BillNumber { get; set; } = string.Empty;

    public DateOnly BillDate { get; set; }

    /// <summary>Fixed when entered: bill date + the customer's credit days at that time.</summary>
    public DateOnly DueDate { get; set; }

    public BalanceSide Side { get; set; } = BalanceSide.Debit;

    /// <summary>Amount still pending on the old bill (or the advance amount). Always positive.</summary>
    public decimal Amount { get; set; }
}
