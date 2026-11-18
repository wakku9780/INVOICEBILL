using SareeBilling.Application.Common;

namespace SareeBilling.Application.Dashboard;

/// <summary>The period buttons at the top of the dashboard.</summary>
public enum DashboardPeriod
{
    Today,
    ThisWeek,
    ThisMonth,
    ThisFinancialYear,
    Custom
}

/// <summary>An inclusive range of calendar days.</summary>
public readonly record struct DateRange(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= From && date <= To;
}

public static class DashboardPeriods
{
    /// <summary>
    /// Turns a period button into dates. Every period ends today; weeks start on Monday and the
    /// financial year on 1 April. <paramref name="customFrom"/>/<paramref name="customTo"/> are
    /// used only for <see cref="DashboardPeriod.Custom"/>.
    /// </summary>
    public static DateRange Resolve(DashboardPeriod period, DateOnly today, DateOnly? customFrom = null, DateOnly? customTo = null)
    {
        switch (period)
        {
            case DashboardPeriod.Today:
                return new DateRange(today, today);

            case DashboardPeriod.ThisWeek:
                var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
                return new DateRange(today.AddDays(-daysSinceMonday), today);

            case DashboardPeriod.ThisMonth:
                return new DateRange(new DateOnly(today.Year, today.Month, 1), today);

            case DashboardPeriod.ThisFinancialYear:
                var startYear = today.Month >= 4 ? today.Year : today.Year - 1;
                return new DateRange(new DateOnly(startYear, 4, 1), today);

            case DashboardPeriod.Custom:
                if (customFrom is not { } from || customTo is not { } to)
                {
                    throw new ValidationException(["Choose both a From and a To date."]);
                }

                if (from > to)
                {
                    throw new ValidationException(["The From date must be on or before the To date."]);
                }

                return new DateRange(from, to);

            default:
                throw new ArgumentOutOfRangeException(nameof(period), period, null);
        }
    }
}
