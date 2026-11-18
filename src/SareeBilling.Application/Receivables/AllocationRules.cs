using SareeBilling.Domain.Common;

namespace SareeBilling.Application.Receivables;

/// <summary>
/// Bill-wise settlement rules. Pure functions, so they are easy to test and are applied the
/// same way on screen (suggestions) and when saving (checks inside the database transaction).
/// </summary>
public static class AllocationRules
{
    /// <summary>Fills the oldest unpaid bills first with <paramref name="amount"/>.</summary>
    public static IReadOnlyList<AllocationLine> AutoFill(CustomerAccount account, decimal amount)
    {
        var lines = new List<AllocationLine>();
        var left = Money.Round2(amount);

        foreach (var bill in account.OpenBills)
        {
            if (left <= 0)
            {
                break;
            }

            var pay = Math.Min(bill.Pending, left);
            lines.Add(new AllocationLine(bill.Ref, pay));
            left -= pay;
        }

        return lines;
    }

    /// <summary>
    /// Checks a set of lines against the customer's current bills: every bill must be open, each
    /// amount positive with at most 2 decimals and no more than what is pending, no bill twice,
    /// and the lines together no more than <paramref name="available"/>.
    /// </summary>
    public static List<string> Check(CustomerAccount account, IReadOnlyList<AllocationLine> lines, decimal available)
    {
        var errors = new List<string>();
        var bills = account.Bills.ToDictionary(b => b.Ref);

        foreach (var group in lines.GroupBy(l => l.Bill))
        {
            if (group.Count() > 1)
            {
                errors.Add("The same bill is listed twice.");
                continue;
            }

            var line = group.Single();
            if (!bills.TryGetValue(line.Bill, out var bill))
            {
                errors.Add("A selected bill no longer exists or was cancelled. Please choose the customer again.");
                continue;
            }

            if (line.Amount <= 0)
            {
                errors.Add($"Bill {bill.Number}: the amount must be more than zero.");
            }
            else if (line.Amount != Money.Round2(line.Amount))
            {
                errors.Add($"Bill {bill.Number}: use at most 2 decimal places.");
            }
            else if (line.Amount > bill.Pending)
            {
                errors.Add($"Bill {bill.Number}: only {IndianFormat.Amount(bill.Pending)} is pending, " +
                           $"so {IndianFormat.Amount(line.Amount)} cannot be settled against it.");
            }
        }

        var total = lines.Sum(l => l.Amount);
        if (total > available)
        {
            errors.Add($"The bills add up to {IndianFormat.Amount(total)}, which is more than the " +
                       $"{IndianFormat.Amount(available)} available.");
        }

        return errors;
    }

    /// <summary>
    /// Spreads the bill amounts over the customer's unused receipts and advances, oldest credit
    /// first (used when an advance is adjusted against bills).
    /// </summary>
    public static IReadOnlyList<PlannedAllocation> Distribute(CustomerAccount account, IReadOnlyList<AllocationLine> lines)
    {
        var credits = account.UnusedCredits.Select(c => (c.Ref, Left: c.Unused)).ToList();
        var result = new List<PlannedAllocation>();
        var index = 0;

        foreach (var line in lines)
        {
            var due = line.Amount;
            while (due > 0)
            {
                if (index >= credits.Count)
                {
                    throw new InvalidOperationException("Not enough advance to cover the selected bills.");
                }

                var (credit, left) = credits[index];
                var take = Math.Min(left, due);
                result.Add(new PlannedAllocation(credit, line.Bill, take));
                due -= take;
                left -= take;
                credits[index] = (credit, left);
                if (left == 0)
                {
                    index++;
                }
            }
        }

        return result;
    }
}
