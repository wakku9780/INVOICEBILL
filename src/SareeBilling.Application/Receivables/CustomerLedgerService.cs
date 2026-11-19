using System.Globalization;
using System.Text;
using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Common;
using SareeBilling.Domain.Common;

namespace SareeBilling.Application.Receivables;

public enum LedgerEntryType
{
    OpeningBill,
    OpeningAdvance,
    SalesInvoice,
    Receipt,
    CreditNote
}

/// <summary>One row of a customer ledger. <see cref="Balance"/> is the running balance after this row (+ = Dr).</summary>
public sealed record LedgerEntry(
    DateOnly Date,
    LedgerEntryType Type,
    string Reference,
    string Particulars,
    decimal Debit,
    decimal Credit,
    decimal Balance,
    int SourceId);

public sealed record CustomerLedger(
    int CustomerId,
    string CustomerName,
    string City,
    int CreditDays,
    DateOnly From,
    DateOnly To,
    decimal OpeningBalance,
    IReadOnlyList<LedgerEntry> Entries,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal ClosingBalance);

public interface ICustomerLedgerService
{
    /// <summary>
    /// The customer's ledger for the period: the balance brought forward from before
    /// <paramref name="from"/>, then every invoice, receipt, credit note and opening entry with a running balance.
    /// Cancelled documents are left out.
    /// </summary>
    Task<CustomerLedger> GetAsync(int customerId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public sealed class CustomerLedgerService(IReceivablesRepository repository) : ICustomerLedgerService
{
    public async Task<CustomerLedger> GetAsync(int customerId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new ValidationException(["The From date must be on or before the To date."]);
        }

        var account = await repository.GetAccountAsync(customerId, cancellationToken)
                      ?? throw new ValidationException(["This customer no longer exists."]);
        return Build(account, from, to);
    }

    public static CustomerLedger Build(CustomerAccount account, DateOnly from, DateOnly to)
    {
        var all = account.Bills
            .Select(b => (b.Date, Type: b.Ref.Kind == BillKind.Invoice ? LedgerEntryType.SalesInvoice : LedgerEntryType.OpeningBill,
                          b.Number, b.Particulars, Debit: b.Total, Credit: 0m, b.Ref.Id))
            .Concat(account.Credits
                .Select(c => (c.Date, Type: c.Ref.Kind switch
                              {
                                  CreditKind.Receipt => LedgerEntryType.Receipt,
                                  CreditKind.CreditNote => LedgerEntryType.CreditNote,
                                  _ => LedgerEntryType.OpeningAdvance
                              },
                              c.Number, c.Particulars, Debit: 0m, Credit: c.Amount, c.Ref.Id)))
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Type switch
            {
                LedgerEntryType.OpeningBill or LedgerEntryType.OpeningAdvance => 0,
                LedgerEntryType.SalesInvoice => 1,
                LedgerEntryType.CreditNote => 2,
                _ => 3
            })
            .ThenBy(e => e.Id)
            .ToList();

        var opening = all.Where(e => e.Date < from).Sum(e => e.Debit - e.Credit);
        var running = opening;
        var entries = new List<LedgerEntry>();
        foreach (var e in all.Where(e => e.Date >= from && e.Date <= to))
        {
            running += e.Debit - e.Credit;
            entries.Add(new LedgerEntry(e.Date, e.Type, e.Number, e.Particulars, e.Debit, e.Credit, running, e.Id));
        }

        return new CustomerLedger(
            account.CustomerId, account.CustomerName, account.City, account.CreditDays, from, to, opening, entries,
            entries.Sum(e => e.Debit), entries.Sum(e => e.Credit), running);
    }
}

public static class LedgerFormat
{
    /// <summary>"12,000.00 Dr", "500.00 Cr" or "0.00".</summary>
    public static string Balance(decimal balance) => balance switch
    {
        > 0 => IndianFormat.Amount(balance) + " Dr",
        < 0 => IndianFormat.Amount(-balance) + " Cr",
        _ => IndianFormat.Amount(0)
    };

    public static string TypeLabel(LedgerEntryType type) => type switch
    {
        LedgerEntryType.OpeningBill => "Opening bill",
        LedgerEntryType.OpeningAdvance => "Opening advance",
        LedgerEntryType.SalesInvoice => "Sales invoice",
        LedgerEntryType.Receipt => "Receipt",
        LedgerEntryType.CreditNote => "Credit note",
        _ => type.ToString()
    };

    public static string Date(DateOnly date) => date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    /// <summary>The ledger as CSV (opens in Excel). Amounts are plain numbers so Excel can add them up.</summary>
    public static string ToCsv(CustomerLedger ledger)
    {
        var sb = new StringBuilder();
        Row(sb, "Customer ledger", ledger.CustomerName);
        Row(sb, "Period", $"{Date(ledger.From)} to {Date(ledger.To)}");
        sb.AppendLine();
        Row(sb, "Date", "Type", "Ref. no.", "Particulars", "Debit", "Credit", "Balance", "Dr/Cr");
        Row(sb, Date(ledger.From), "", "", "Opening balance", "", "", Plain(Math.Abs(ledger.OpeningBalance)), Side(ledger.OpeningBalance));
        foreach (var e in ledger.Entries)
        {
            Row(sb, Date(e.Date), TypeLabel(e.Type), e.Reference, e.Particulars,
                e.Debit == 0 ? "" : Plain(e.Debit), e.Credit == 0 ? "" : Plain(e.Credit),
                Plain(Math.Abs(e.Balance)), Side(e.Balance));
        }

        Row(sb, "", "", "", "Totals for the period", Plain(ledger.TotalDebit), Plain(ledger.TotalCredit), "", "");
        Row(sb, Date(ledger.To), "", "", "Closing balance", "", "", Plain(Math.Abs(ledger.ClosingBalance)), Side(ledger.ClosingBalance));
        return sb.ToString();
    }

    internal static string Side(decimal balance) => balance > 0 ? "Dr" : balance < 0 ? "Cr" : "";

    internal static string Plain(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    internal static void Row(StringBuilder sb, params string[] cells) =>
        sb.AppendLine(string.Join(",", cells.Select(Quote)));

    /// <summary>
    /// Quotes a CSV cell. A leading =, +, - or @ is prefixed with an apostrophe so Excel never runs
    /// a customer name as a formula.
    /// </summary>
    private static string Quote(string cell)
    {
        if (cell.Length > 0 && "=+-@".Contains(cell[0]) && !decimal.TryParse(cell, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            cell = "'" + cell;
        }

        return cell.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;
    }
}
