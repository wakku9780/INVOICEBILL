using System.Text;
using SareeBilling.Application.Abstractions;

namespace SareeBilling.Application.Receivables;

/// <summary>Pending amounts split by how late they are (days past the due date).</summary>
public sealed record AgeingBuckets(decimal NotDue, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90)
{
    public static readonly AgeingBuckets Zero = new(0, 0, 0, 0, 0);

    public decimal Overdue => Days1To30 + Days31To60 + Days61To90 + Over90;

    public decimal Total => NotDue + Overdue;

    public AgeingBuckets Add(decimal amount, int daysOverdue) => daysOverdue switch
    {
        <= 0 => this with { NotDue = NotDue + amount },
        <= 30 => this with { Days1To30 = Days1To30 + amount },
        <= 60 => this with { Days31To60 = Days31To60 + amount },
        <= 90 => this with { Days61To90 = Days61To90 + amount },
        _ => this with { Over90 = Over90 + amount }
    };

    public AgeingBuckets Plus(AgeingBuckets other) => new(
        NotDue + other.NotDue, Days1To30 + other.Days1To30, Days31To60 + other.Days31To60,
        Days61To90 + other.Days61To90, Over90 + other.Over90);
}

public sealed record OutstandingBill(BillRef Ref, string Number, DateOnly Date, DateOnly DueDate, decimal Total, decimal Pending, int DaysOverdue);

public sealed record OutstandingRow(
    int CustomerId,
    string CustomerName,
    string City,
    decimal Pending,
    decimal Advance,
    decimal Balance,
    AgeingBuckets Ageing,
    IReadOnlyList<OutstandingBill> Bills);

public sealed record OutstandingReport(DateOnly AsOf, IReadOnlyList<OutstandingRow> Rows)
{
    public decimal TotalPending => Rows.Sum(r => r.Pending);
    public decimal TotalAdvance => Rows.Sum(r => r.Advance);
    public decimal TotalBalance => Rows.Sum(r => r.Balance);
    public AgeingBuckets TotalAgeing => Rows.Aggregate(AgeingBuckets.Zero, (sum, r) => sum.Plus(r.Ageing));
}

public interface IOutstandingReportService
{
    /// <summary>Every customer with an unpaid bill or an unused advance, largest balance first.</summary>
    Task<OutstandingReport> GetAsync(DateOnly asOf, CancellationToken cancellationToken = default);
}

public sealed class OutstandingReportService(IReceivablesRepository repository) : IOutstandingReportService
{
    public async Task<OutstandingReport> GetAsync(DateOnly asOf, CancellationToken cancellationToken = default) =>
        Build(await repository.GetAllAccountsAsync(cancellationToken), asOf);

    public static OutstandingReport Build(IEnumerable<CustomerAccount> accounts, DateOnly asOf)
    {
        var rows = new List<OutstandingRow>();
        foreach (var account in accounts)
        {
            if (account.Pending == 0 && account.Advance == 0)
            {
                continue;
            }

            var bills = account.OpenBills
                .Select(b => new OutstandingBill(b.Ref, b.Number, b.Date, b.DueDate, b.Total, b.Pending, b.DaysOverdue(asOf)))
                .ToList();
            var ageing = bills.Aggregate(AgeingBuckets.Zero, (sum, b) => sum.Add(b.Pending, b.DaysOverdue));

            rows.Add(new OutstandingRow(
                account.CustomerId, account.CustomerName, account.City,
                account.Pending, account.Advance, account.Balance, ageing, bills));
        }

        return new OutstandingReport(asOf, rows
            .OrderByDescending(r => r.Balance)
            .ThenBy(r => r.CustomerName, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    public static string ToCsv(OutstandingReport report)
    {
        var sb = new StringBuilder();
        LedgerFormat.Row(sb, "Outstanding receivables as of", LedgerFormat.Date(report.AsOf));
        sb.AppendLine();
        LedgerFormat.Row(sb, "Customer", "City", "Pending", "Advance", "Balance", "Dr/Cr",
            "Not due", "1-30 days", "31-60 days", "61-90 days", "Over 90 days");
        foreach (var r in report.Rows)
        {
            LedgerFormat.Row(sb, r.CustomerName, r.City, LedgerFormat.Plain(r.Pending), LedgerFormat.Plain(r.Advance),
                LedgerFormat.Plain(Math.Abs(r.Balance)), LedgerFormat.Side(r.Balance),
                LedgerFormat.Plain(r.Ageing.NotDue), LedgerFormat.Plain(r.Ageing.Days1To30), LedgerFormat.Plain(r.Ageing.Days31To60),
                LedgerFormat.Plain(r.Ageing.Days61To90), LedgerFormat.Plain(r.Ageing.Over90));
        }

        var t = report.TotalAgeing;
        LedgerFormat.Row(sb, "Total", "", LedgerFormat.Plain(report.TotalPending), LedgerFormat.Plain(report.TotalAdvance),
            LedgerFormat.Plain(Math.Abs(report.TotalBalance)), LedgerFormat.Side(report.TotalBalance),
            LedgerFormat.Plain(t.NotDue), LedgerFormat.Plain(t.Days1To30), LedgerFormat.Plain(t.Days31To60),
            LedgerFormat.Plain(t.Days61To90), LedgerFormat.Plain(t.Over90));
        return sb.ToString();
    }
}
