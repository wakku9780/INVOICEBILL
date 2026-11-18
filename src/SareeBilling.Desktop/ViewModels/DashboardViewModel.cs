using System.Globalization;
using System.Windows.Input;
using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Backups;
using SareeBilling.Application.Common;
using SareeBilling.Application.CompanyProfiles;
using SareeBilling.Application.Dashboard;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Navigation;
using SareeBilling.Desktop.Services;
using SareeBilling.Domain.Common;

namespace SareeBilling.Desktop.ViewModels;

/// <summary>A period button on the dashboard.</summary>
public sealed class PeriodOption : ObservableObject
{
    private bool _isSelected;

    public PeriodOption(DashboardPeriod period, string label)
    {
        Period = period;
        Label = label;
    }

    public DashboardPeriod Period { get; }

    public string Label { get; }

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

public sealed record TopCustomerRow(string Name, string SalesText, string InvoicesText, double BarWidth);

public sealed record RecentInvoiceRow(int InvoiceId, string InvoiceNumber, string DateText, string BuyerName, string AmountText, bool IsCancelled);

public sealed record TrendBar(string MonthLabel, string AmountText, double BarHeight, double CollectionHeight, string ToolTip);

public sealed record OverdueRow(int CustomerId, string CustomerName, string BillNumber, string LateText, string AmountText);

/// <summary>Collections for the period and what customers owe today, ready to show.</summary>
public sealed record MoneyPosition(
    string CollectionsText,
    string CollectionsNote,
    string ReceivablesText,
    string ReceivablesNote,
    string OverdueText,
    string OverdueNote,
    string AdvanceText,
    string AsOfText,
    IReadOnlyList<OverdueRow> Overdue)
{
    public bool HasNoOverdue => Overdue.Count == 0;
}

public sealed class DashboardViewModel : PageViewModel
{
    /// <summary>After this many days without a manual backup, the dashboard shows a reminder.</summary>
    private const int BackupReminderDays = 7;

    private const double TrendBarMaxHeight = 140;
    private const double TopCustomerBarMaxWidth = 120;

    private readonly ICompanyProfileService _companyProfiles;
    private readonly IBackupService _backups;
    private readonly IDashboardService _dashboard;
    private readonly IInvoiceActions _invoiceActions;
    private readonly TimeProvider _timeProvider;

    private bool _isCompanySetupNeeded;
    private bool _isBackupOverdue;
    private string _backupReminderText = string.Empty;
    private bool _isLoading;
    private string? _errorText;
    private DateTime? _customFrom;
    private DateTime? _customTo;
    private string _periodText = string.Empty;
    private string _salesText = "0.00";
    private string _salesNote = string.Empty;
    private string _taxableText = "0.00";
    private string _gstText = "0.00";
    private string _gstNote = string.Empty;
    private string _customersText = "0";
    private string _customersNote = string.Empty;
    private IReadOnlyList<TopCustomerRow> _topCustomers = [];
    private IReadOnlyList<RecentInvoiceRow> _recentInvoices = [];
    private IReadOnlyList<TrendBar> _trend = [];
    private string _trendCaption = string.Empty;
    private MoneyPosition? _money;

    public DashboardViewModel(
        INavigationService navigation,
        IAppPaths paths,
        ICompanyProfileService companyProfiles,
        IBackupService backups,
        IDashboardService dashboard,
        IInvoiceActions invoiceActions,
        TimeProvider timeProvider)
    {
        _companyProfiles = companyProfiles;
        _backups = backups;
        _dashboard = dashboard;
        _invoiceActions = invoiceActions;
        _timeProvider = timeProvider;
        DatabasePath = paths.DatabasePath;

        Periods =
        [
            new(DashboardPeriod.Today, "Today"),
            new(DashboardPeriod.ThisWeek, "This Week"),
            new(DashboardPeriod.ThisMonth, "This Month") { IsSelected = true },
            new(DashboardPeriod.ThisFinancialYear, "This Financial Year"),
            new(DashboardPeriod.Custom, "Custom Range")
        ];

        var today = Today.ToDateTime(TimeOnly.MinValue);
        _customFrom = today.AddDays(-30);
        _customTo = today;

        OpenPageCommand = new RelayCommand<AppPage>(page => navigation.NavigateTo(page));
        SelectPeriodCommand = new AsyncRelayCommand<PeriodOption>(SelectPeriodAsync);
        ApplyCustomRangeCommand = new AsyncRelayCommand(LoadSummaryAsync);
        OpenInvoiceCommand = new AsyncRelayCommand<RecentInvoiceRow>(row => _invoiceActions.PreviewAsync(row.InvoiceId));
        OpenOverdueCommand = new RelayCommand<OverdueRow>(row => navigation.NavigateTo(AppPage.CustomerLedger, row.CustomerId));
    }

    public override string Title => "Dashboard";

    public string DatabasePath { get; }

    /// <summary>Quick-action buttons pass the target AppPage as CommandParameter.</summary>
    public ICommand OpenPageCommand { get; }

    public ICommand SelectPeriodCommand { get; }

    public ICommand ApplyCustomRangeCommand { get; }

    public ICommand OpenInvoiceCommand { get; }

    public ICommand OpenOverdueCommand { get; }

    public MoneyPosition? Position { get => _money; private set => SetProperty(ref _money, value); }

    public IReadOnlyList<PeriodOption> Periods { get; }

    private PeriodOption SelectedPeriod => Periods.First(p => p.IsSelected);

    public bool IsCustomPeriod => SelectedPeriod.Period == DashboardPeriod.Custom;

    public DateTime? CustomFrom { get => _customFrom; set => SetProperty(ref _customFrom, value); }

    public DateTime? CustomTo { get => _customTo; set => SetProperty(ref _customTo, value); }

    /// <summary>True until company details are saved; invoices cannot be made before that.</summary>
    public bool IsCompanySetupNeeded
    {
        get => _isCompanySetupNeeded;
        private set => SetProperty(ref _isCompanySetupNeeded, value);
    }

    public bool IsBackupOverdue { get => _isBackupOverdue; private set => SetProperty(ref _isBackupOverdue, value); }

    public string BackupReminderText { get => _backupReminderText; private set => SetProperty(ref _backupReminderText, value); }

    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

    public string? ErrorText { get => _errorText; private set => SetProperty(ref _errorText, value); }

    public string PeriodText { get => _periodText; private set => SetProperty(ref _periodText, value); }

    public string SalesText { get => _salesText; private set => SetProperty(ref _salesText, value); }

    public string SalesNote { get => _salesNote; private set => SetProperty(ref _salesNote, value); }

    public string TaxableText { get => _taxableText; private set => SetProperty(ref _taxableText, value); }

    public string GstText { get => _gstText; private set => SetProperty(ref _gstText, value); }

    public string GstNote { get => _gstNote; private set => SetProperty(ref _gstNote, value); }

    public string CustomersText { get => _customersText; private set => SetProperty(ref _customersText, value); }

    public string CustomersNote { get => _customersNote; private set => SetProperty(ref _customersNote, value); }

    public IReadOnlyList<TopCustomerRow> TopCustomers
    {
        get => _topCustomers;
        private set
        {
            if (SetProperty(ref _topCustomers, value))
            {
                OnPropertyChanged(nameof(HasNoTopCustomers));
            }
        }
    }

    public bool HasNoTopCustomers => TopCustomers.Count == 0;

    public IReadOnlyList<RecentInvoiceRow> RecentInvoices
    {
        get => _recentInvoices;
        private set
        {
            if (SetProperty(ref _recentInvoices, value))
            {
                OnPropertyChanged(nameof(HasNoRecentInvoices));
                OnPropertyChanged(nameof(HasRecentInvoices));
            }
        }
    }

    public bool HasNoRecentInvoices => RecentInvoices.Count == 0;

    public bool HasRecentInvoices => RecentInvoices.Count > 0;

    public IReadOnlyList<TrendBar> Trend { get => _trend; private set => SetProperty(ref _trend, value); }

    public string TrendCaption { get => _trendCaption; private set => SetProperty(ref _trendCaption, value); }

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

    public override async Task InitializeAsync()
    {
        IsCompanySetupNeeded = await _companyProfiles.GetAsync() is null;
        if (!IsCompanySetupNeeded)
        {
            UpdateBackupReminder();
        }

        await LoadSummaryAsync();
    }

    private async Task SelectPeriodAsync(PeriodOption option)
    {
        foreach (var period in Periods)
        {
            period.IsSelected = ReferenceEquals(period, option);
        }

        OnPropertyChanged(nameof(IsCustomPeriod));

        // A custom range is loaded with its Show button, after both dates are chosen.
        if (option.Period != DashboardPeriod.Custom)
        {
            await LoadSummaryAsync();
        }
    }

    private async Task LoadSummaryAsync()
    {
        DateRange range;
        try
        {
            range = DashboardPeriods.Resolve(
                SelectedPeriod.Period,
                Today,
                CustomFrom is { } from ? DateOnly.FromDateTime(from) : null,
                CustomTo is { } to ? DateOnly.FromDateTime(to) : null);
        }
        catch (ValidationException ex)
        {
            ErrorText = ex.Message;
            return;
        }

        ErrorText = null;
        IsLoading = true;
        try
        {
            Show(await _dashboard.GetSummaryAsync(range));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Show(DashboardSummary summary)
    {
        PeriodText = summary.Range.From == summary.Range.To
            ? FormatDate(summary.Range.From)
            : $"{FormatDate(summary.Range.From)} to {FormatDate(summary.Range.To)}";

        SalesText = IndianFormat.Amount(summary.Sales);
        SalesNote = summary.CancelledCount == 0
            ? Plural(summary.InvoiceCount, "invoice")
            : $"{Plural(summary.InvoiceCount, "invoice")} · {summary.CancelledCount} cancelled";
        TaxableText = IndianFormat.Amount(summary.TaxableValue);
        GstText = IndianFormat.Amount(summary.GstCollected);
        GstNote = "CGST + SGST + IGST";
        CustomersText = summary.CustomersBilled.ToString(CultureInfo.InvariantCulture);
        CustomersNote = summary.InvoiceCount == 0
            ? "No sales in this period"
            : $"Average bill {IndianFormat.Amount(Money.Round2(summary.Sales / summary.InvoiceCount))}";

        var topSales = summary.TopCustomers.Count == 0 ? 0m : summary.TopCustomers.Max(c => c.Sales);
        TopCustomers = summary.TopCustomers
            .Select(c => new TopCustomerRow(
                c.Name,
                IndianFormat.Amount(c.Sales),
                Plural(c.InvoiceCount, "invoice"),
                topSales == 0 ? 0 : (double)(c.Sales / topSales) * TopCustomerBarMaxWidth))
            .ToList();

        RecentInvoices = summary.RecentInvoices
            .Select(r => new RecentInvoiceRow(
                r.InvoiceId, r.InvoiceNumber, FormatDate(r.InvoiceDate), r.BuyerName,
                IndianFormat.Amount(r.GrandTotal), r.IsCancelled))
            .ToList();

        var topMonth = summary.Trend.Count == 0 ? 0m : summary.Trend.Max(m => Math.Max(m.Sales, m.Collections));
        double Height(decimal value) =>
            value <= 0 || topMonth == 0 ? 0 : Math.Max((double)(value / topMonth) * TrendBarMaxHeight, 3); // always at least a sliver
        Trend = summary.Trend
            .Select(m => new TrendBar(
                m.Month.ToString("MMM", CultureInfo.InvariantCulture),
                m.Sales > 0 ? IndianFormat.Amount(m.Sales) : string.Empty,
                Height(m.Sales),
                Height(m.Collections),
                $"{m.Month.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}\nSales: {IndianFormat.Amount(m.Sales)} " +
                $"({Plural(m.InvoiceCount, "invoice")})\nCollected: {IndianFormat.Amount(m.Collections)}"))
            .ToList();
        TrendCaption = summary.Trend.Count == 0
            ? string.Empty
            : $"Sales and money collected by month, {summary.Trend[0].Month.ToString("MMM yyyy", CultureInfo.InvariantCulture)} to " +
              $"{summary.Trend[^1].Month.ToString("MMM yyyy", CultureInfo.InvariantCulture)} (cancelled documents not included)";

        var r = summary.Receivables;
        Position = new MoneyPosition(
            IndianFormat.Amount(summary.Collections),
            Plural(summary.ReceiptCount, "receipt"),
            IndianFormat.Amount(r.Pending),
            r.CustomersOwing == 1 ? "1 customer owes" : $"{r.CustomersOwing} customers owe",
            IndianFormat.Amount(r.Overdue),
            r.OverdueBillCount == 0 ? "Nothing overdue" : Plural(r.OverdueBillCount, "bill") + " past due date",
            IndianFormat.Amount(r.Advance),
            "As of today, " + FormatDate(r.AsOf),
            r.MostOverdue.Select(b => new OverdueRow(
                b.CustomerId, b.CustomerName, b.BillNumber,
                $"due {FormatDate(b.DueDate)} · {b.DaysOverdue} {(b.DaysOverdue == 1 ? "day" : "days")} late",
                IndianFormat.Amount(b.Pending))).ToList());
    }

    private void UpdateBackupReminder()
    {
        var last = _backups.GetLastManualBackupTime();
        var now = _timeProvider.GetLocalNow().DateTime;
        if (last is null)
        {
            IsBackupOverdue = true;
            BackupReminderText = "You have not made a backup yet.";
        }
        else if ((now - last.Value).TotalDays >= BackupReminderDays)
        {
            IsBackupOverdue = true;
            BackupReminderText = $"Last backup was on {last.Value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)} " +
                                 $"({(int)(now - last.Value).TotalDays} days ago).";
        }
    }

    private static string FormatDate(DateOnly date) => date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);

    private static string Plural(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count.ToString("#,##0", CultureInfo.InvariantCulture)} {noun}s";
}
