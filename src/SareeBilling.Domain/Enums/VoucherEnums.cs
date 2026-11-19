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
    Receipt = 1,
    CreditNote = 2
}

/// <summary>What a credit note is for. Values are stored.</summary>
public enum CreditNoteKind
{
    /// <summary>Goods sent back: quantities returned from invoice lines.</summary>
    GoodsReturn = 1,

    /// <summary>No goods back: the value of invoice lines is reduced (rate difference or discount).</summary>
    AmountOnly = 2
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
