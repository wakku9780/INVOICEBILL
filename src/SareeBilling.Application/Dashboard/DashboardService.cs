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

/// <summary>Sales of one calendar month; <see cref="Month"/> is the first day of that month.</summary>
public sealed record MonthlySales(DateOnly Month, decimal Sales, int InvoiceCount);

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
    IReadOnlyList<MonthlySales> Trend);

public interface IDashboardService
{
    /// <summary>
    /// Figures for the chosen period, worked out from the saved invoices. Cancelled invoices are
    /// counted separately and never included in any amount.
    /// </summary>
    Task<DashboardSummary> GetSummaryAsync(DateRange range, CancellationToken cancellationToken = default);
}

public sealed class DashboardService(IInvoiceRepository invoices) : IDashboardService
{
    public const int TopCustomerCount = 5;
    public const int RecentInvoiceCount = 8;
    public const int TrendMonths = 6;

    public async Task<DashboardSummary> GetSummaryAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        // One read covers both the period and the six-month trend that ends in the period's last month.
        var trendStart = new DateOnly(range.To.Year, range.To.Month, 1).AddMonths(-(TrendMonths - 1));
        var readFrom = range.From < trendStart ? range.From : trendStart;
        var rows = await invoices.ListSalesAsync(readFrom, range.To, cancellationToken);

        return Summarise(range, trendStart, rows);
    }

    private static DashboardSummary Summarise(DateRange range, DateOnly trendStart, IReadOnlyList<SalesRow> rows)
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
            trend.Add(new MonthlySales(month, Money.Round2(monthRows.Sum(r => r.GrandTotal)), monthRows.Count));
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
