using SareeBilling.Domain.Enums;

namespace SareeBilling.Domain.Entities;

/// <summary>Last number used per voucher series (Receipt: R-1, R-2 ...). Numbers never restart.</summary>
public class VoucherCounter
{
    public VoucherType VoucherType { get; set; }

    public int LastNumber { get; set; }
}
