namespace SareeBilling.Domain.Enums;

/// <summary>How a customer paid.</summary>
public enum PaymentMode
{
    Cash = 1,
    BankTransfer = 2,
    Cheque = 3
}

/// <summary>A receipt (and later other vouchers) is never edited, only cancelled.</summary>
public enum VoucherStatus
{
    Posted = 1,
    Cancelled = 2
}

/// <summary>Voucher series with their own running number. Values are stored; never renumber.</summary>
public enum VoucherType
{
    Receipt = 1
}

/// <summary>
/// Side of an opening entry: Debit = the customer owed money (an old unpaid bill);
/// Credit = the customer had paid in advance.
/// </summary>
public enum BalanceSide
{
    Debit = 1,
    Credit = 2
}
