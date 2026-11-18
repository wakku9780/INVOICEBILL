using SareeBilling.Application.Abstractions;
using SareeBilling.Domain.Common;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Application.Dashboard;

/// <summary>One sales invoice reduced to what the dashboard needs.</summary>
public sealed record SalesRow(
    int InvoiceId,
    string InvoiceNumber,
    int SequenceNumber,
    DateOnly InvoiceDate,
    int CustomerId,
    string BuyerName,
    decimal TaxableTotal,
    decimal TotalTax,
    decimal GrandTotal,
    InvoiceStatus Status);

public sealed record TopCustomer(int CustomerId, string Name, int InvoiceCount, decimal Sales);

public sealed record RecentInvoice(
    int InvoiceId, string InvoiceNumber, DateOnly InvoiceDate, string BuyerName, decimal GrandTotal, bool IsCancelled);

/// <summary>Sales and collections of one calendar month; <see cref="Month"/> is the first day of that month.</summary>
public sealed record MonthlySales(DateOnly Month, decimal Sales, int InvoiceCount, decimal Collections);

public sealed record OverdueBill(int CustomerId, string CustomerName, string BillNumber, DateOnly DueDate, int DaysOverdue, decimal Pending);

/// <summary>What customers owe as of today (not limited to the selected period).</summary>
public sealed record ReceivablesPosition(
    DateOnly AsOf,
    decimal Pending,
    decimal Advance,
    decimal Overdue,
    int CustomersOwing,
    int OverdueBillCount,
    IReadOnlyList<OverdueBill> MostOverdue);

public sealed record DashboardSummary(
    DateRange Range,
    decimal Sales,
    decimal TaxableValue,
    decimal GstCollected,
    int InvoiceCount,
    int CancelledCount,
    int CustomersBilled,
    IReadOnlyList<TopCustomer> TopCustomers,
    IReadOnlyList<RecentInvoice> RecentInvoices,
    IReadOnlyList<MonthlySales> Trend)
{
    /// <summary>Receipts (not cancelled) dated in the period.</summary>
    public decimal Collections { get; init; }

    public int ReceiptCount { get; init; }

    public ReceivablesPosition Receivables { get; init; } = new(default, 0, 0, 0, 0, 0, []);
}

public interface IDashboardService
{
    /// <summary>
    /// Figures for the chosen period, worked out from the saved invoices and receipts. Cancelled
    /// documents are counted separately and never included in any amount. Receivables are as of today.
    /// </summary>
    Task<DashboardSummary> GetSummaryAsync(DateRange range, CancellationToken cancellationToken = default);
}

public sealed class DashboardService(IInvoiceRepository invoices, IReceivablesRepository receivables, TimeProvider timeProvider)
    : IDashboardService
{
    public const int TopCustomerCount = 5;
    public const int RecentInvoiceCount = 8;
    public const int TrendMonths = 6;
    public const int OverdueListCount = 6;

    public async Task<DashboardSummary> GetSummaryAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        // One read covers both the period and the six-month trend that ends in the period's last month.
        var trendStart = new DateOnly(range.To.Year, range.To.Month, 1).AddMonths(-(TrendMonths - 1));
        var readFrom = range.From < trendStart ? range.From : trendStart;
        var rows = await invoices.ListSalesAsync(readFrom, range.To, cancellationToken);
        var receipts = await receivables.ListReceiptsAsync(readFrom, range.To, cancellationToken);
        var accounts = await receivables.GetAllAccountsAsync(cancellationToken);
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

        var summary = Summarise(range, trendStart, rows, receipts);
        var inPeriod = receipts.Where(r => range.Contains(r.ReceiptDate)).ToList();
        return summary with
        {
            Collections = Money.Round2(inPeriod.Sum(r => r.Amount)),
            ReceiptCount = inPeriod.Count,
            Receivables = Position(accounts, today)
        };
    }

    internal static ReceivablesPosition Position(IReadOnlyList<Receivables.CustomerAccount> accounts, DateOnly today)
    {
        var overdue = accounts
            .SelectMany(a => a.OpenBills
                .Where(b => b.DaysOverdue(today) > 0)
                .Select(b => new OverdueBill(a.CustomerId, a.CustomerName, b.Number, b.DueDate, b.DaysOverdue(today), b.Pending)))
            .ToList();

        return new ReceivablesPosition(
            today,
            Money.Round2(accounts.Sum(a => a.Pending)),
            Money.Round2(accounts.Sum(a => a.Advance)),
            Money.Round2(overdue.Sum(b => b.Pending)),
            accounts.Count(a => a.Pending > 0),
            overdue.Count,
            overdue.OrderByDescending(b => b.DaysOverdue).ThenByDescending(b => b.Pending).Take(OverdueListCount).ToList());
    }

    private static DashboardSummary Summarise(DateRange range, DateOnly trendStart, IReadOnlyList<SalesRow> rows, IReadOnlyList<Receivables.ReceiptRow> receipts)
    {
        var inPeriod = rows.Where(r => range.Contains(r.InvoiceDate)).ToList();
        var final = inPeriod.Where(r => r.Status == InvoiceStatus.Final).ToList();

        var topCustomers = final
            .GroupBy(r => r.CustomerId)
            .Select(g =>
            {
                // The newest invoice carries the customer's current name.
                var latest = g.OrderByDescending(r => r.InvoiceDate).ThenByDescending(r => r.SequenceNumber).First();
                return new TopCustomer(g.Key, latest.BuyerName, g.Count(), Money.Round2(g.Sum(r => r.GrandTotal)));
            })
            .OrderByDescending(c => c.Sales)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(TopCustomerCount)
            .ToList();

        var recent = inPeriod
            .OrderByDescending(r => r.InvoiceDate)
            .ThenByDescending(r => r.SequenceNumber)
            .Take(RecentInvoiceCount)
            .Select(r => new RecentInvoice(
                r.InvoiceId, r.InvoiceNumber, r.InvoiceDate, r.BuyerName, r.GrandTotal, r.Status == InvoiceStatus.Cancelled))
            .ToList();

        var trend = new List<MonthlySales>(TrendMonths);
        for (var i = 0; i < TrendMonths; i++)
        {
            var month = trendStart.AddMonths(i);
            var monthRows = rows.Where(r => r.Status == InvoiceStatus.Final &&
                                            r.InvoiceDate.Year == month.Year && r.InvoiceDate.Month == month.Month).ToList();
            var collected = receipts.Where(r => r.ReceiptDate.Year == month.Year && r.ReceiptDate.Month == month.Month).Sum(r => r.Amount);
            trend.Add(new MonthlySales(month, Money.Round2(monthRows.Sum(r => r.GrandTotal)), monthRows.Count, Money.Round2(collected)));
        }

        return new DashboardSummary(
            range,
            Sales: Money.Round2(final.Sum(r => r.GrandTotal)),
            TaxableValue: Money.Round2(final.Sum(r => r.TaxableTotal)),
            GstCollected: Money.Round2(final.Sum(r => r.TotalTax)),
            InvoiceCount: final.Count,
            CancelledCount: inPeriod.Count - final.Count,
            CustomersBilled: final.Select(r => r.CustomerId).Distinct().Count(),
            topCustomers,
            recent,
            trend);
    }
}
