namespace SareeBilling.Domain.Gst;

public sealed record IndianState(string Code, string Name)
{
    /// <summary>Shown in drop-downs, e.g. "27 - Maharashtra".</summary>
    public string DisplayName => $"{Code} - {Name}";

    /// <summary>Used by drop-downs for keyboard search and screen readers.</summary>
    public override string ToString() => DisplayName;
}

/// <summary>
/// GST state and union-territory codes. The first two digits of every GSTIN are one of these.
/// (Code 25 was merged into 26 in 2020; code 28 is the pre-2014 Andhra Pradesh code.)
/// </summary>
public static class IndianStates
{
    public static IReadOnlyList<IndianState> All { get; } =
    [
        new("01", "Jammu and Kashmir"),
        new("02", "Himachal Pradesh"),
        new("03", "Punjab"),
        new("04", "Chandigarh"),
        new("05", "Uttarakhand"),
        new("06", "Haryana"),
        new("07", "Delhi"),
        new("08", "Rajasthan"),
        new("09", "Uttar Pradesh"),
        new("10", "Bihar"),
        new("11", "Sikkim"),
        new("12", "Arunachal Pradesh"),
        new("13", "Nagaland"),
        new("14", "Manipur"),
        new("15", "Mizoram"),
        new("16", "Tripura"),
        new("17", "Meghalaya"),
        new("18", "Assam"),
        new("19", "West Bengal"),
        new("20", "Jharkhand"),
        new("21", "Odisha"),
        new("22", "Chhattisgarh"),
        new("23", "Madhya Pradesh"),
        new("24", "Gujarat"),
        new("26", "Dadra and Nagar Haveli and Daman and Diu"),
        new("27", "Maharashtra"),
        new("29", "Karnataka"),
        new("30", "Goa"),
        new("31", "Lakshadweep"),
        new("32", "Kerala"),
        new("33", "Tamil Nadu"),
        new("34", "Puducherry"),
        new("35", "Andaman and Nicobar Islands"),
        new("36", "Telangana"),
        new("37", "Andhra Pradesh"),
        new("38", "Ladakh"),
        new("97", "Other Territory")
    ];

    private static readonly Dictionary<string, IndianState> ByCode = All.ToDictionary(s => s.Code);

    public static IndianState? FindByCode(string? code) =>
        code is not null && ByCode.TryGetValue(code, out var state) ? state : null;
}
