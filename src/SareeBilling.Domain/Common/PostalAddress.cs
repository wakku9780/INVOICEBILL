namespace SareeBilling.Domain.Common;

public static class PostalAddress
{
    /// <summary>
    /// Multi-line address as printed on the invoice (the state is printed separately):
    /// <code>
    /// 24 Bhavani Peth Palki Chowk
    /// Opp. Canara Bank
    /// Pune - 411002
    /// </code>
    /// </summary>
    public static string Format(string line1, string? line2, string city, string? pincode)
    {
        var lines = new List<string>(3) { line1.Trim() };

        if (!string.IsNullOrWhiteSpace(line2))
        {
            lines.Add(line2.Trim());
        }

        lines.Add(string.IsNullOrWhiteSpace(pincode) ? city.Trim() : $"{city.Trim()} - {pincode.Trim()}");

        return string.Join(Environment.NewLine, lines);
    }
}
