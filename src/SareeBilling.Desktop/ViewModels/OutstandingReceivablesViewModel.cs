using System.Globalization;
using System.Windows.Input;
using SareeBilling.Application.Receivables;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Navigation;
using SareeBilling.Desktop.Services;
using SareeBilling.Domain.Common;

namespace SareeBilling.Desktop.ViewModels;

public sealed record OutstandingBillView(OutstandingBill Bill)
{
    public string Number => Bill.Number;
    public string DateText => LedgerFormat.Date(Bill.Date);
    public string DueText => LedgerFormat.Date(Bill.DueDate);
    public string LateText => Bill.DaysOverdue > 0 ? $"{Bill.DaysOverdue} days late" : "Not due";
    public bool IsOverdue => Bill.DaysOverdue > 0;
    public string TotalText => IndianFormat.Amount(Bill.Total);
    public string PendingText => IndianFormat.Amount(Bill.Pending);
}

public sealed record OutstandingRowView(OutstandingRow Row)
{
    public string CustomerName => Row.CustomerName;
    public string City => Row.City;
    public string NotDueText => Amount(Row.Ageing.NotDue);
    public string Days1To30Text => Amount(Row.Ageing.Days1To30);
    public string Days31To60Text => Amount(Row.Ageing.Days31To60);
    public string Days61To90Text => Amount(Row.Ageing.Days61To90);
    public string Over90Text => Amount(Row.Ageing.Over90);
    public string AdvanceText => Amount(Row.Advance);
    public string BalanceText => LedgerFormat.Balance(Row.Balance);
    public bool HasOverdue => Row.Ageing.Overdue > 0;
    public IReadOnlyList<OutstandingBillView> Bills { get; } = Row.Bills.Select(b => new OutstandingBillView(b)).ToList();

    private static string Amount(decimal value) => value == 0 ? string.Empty : IndianFormat.Amount(value);
}

public sealed class OutstandingReceivablesViewModel : PageViewModel
{
    private readonly IOutstandingReportService _reports;
    private readonly IAccountsPrintService _printing;
    private readonly IDocumentActions _documents;
    private readonly INavigationService _navigation;
    private readonly TimeProvider _timeProvider;

    private OutstandingReport? _report;
    private IReadOnlyList<OutstandingRowView> _rows = [];
    private OutstandingRowView? _selected;
    private bool _overdueOnly;

    public OutstandingReceivablesViewModel(
        IOutstandingReportService reports,
        IAccountsPrintService printing,
        IDocumentActions documents,
        INavigationService navigation,
        TimeProvider timeProvider)
    {
        _reports = reports;
        _printing = printing;
        _documents = documents;
        _navigation = navigation;
        _timeProvider = timeProvider;

        OpenLedgerCommand = new RelayCommand(() => _navigation.NavigateTo(AppPage.CustomerLedger, Selected!.Row.CustomerId), () => Selected is not null);
        ReceivePaymentCommand = new RelayCommand(() => _navigation.NavigateTo(AppPage.ReceivePayment, Selected!.Row.CustomerId), () => Selected is not null);
        PrintCommand = new AsyncRelayCommand(PrintAsync, () => _report is not null);
        ExportCommand = new RelayCommand(Export, () => _report is not null);
        RefreshCommand = new AsyncRelayCommand(InitializeAsync);
    }

    public override string Title => "Outstanding Receivables";

    public ICommand OpenLedgerCommand { get; }
    public ICommand ReceivePaymentCommand { get; }
    public ICommand PrintCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand RefreshCommand { get; }

    public IReadOnlyList<OutstandingRowView> Rows { get => _rows; private set => SetProperty(ref _rows, value); }

    public OutstandingRowView? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    public bool OverdueOnly
    {
        get => _overdueOnly;
        set
        {
            if (SetProperty(ref _overdueOnly, value))
            {
                ShowRows();
            }
        }
    }

    public bool IsEmpty => _report is { Rows.Count: 0 };

    public string AsOfText => _report is null ? string.Empty : "As of " + _report.AsOf.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
    public string TotalPendingText => _report is null ? string.Empty : IndianFormat.Amount(_report.TotalPending);
    public string TotalOverdueText => _report is null ? string.Empty : IndianFormat.Amount(_report.TotalAgeing.Overdue);
    public string TotalAdvanceText => _report is null ? string.Empty : IndianFormat.Amount(_report.TotalAdvance);
    public string TotalBalanceText => _report is null ? string.Empty : LedgerFormat.Balance(_report.TotalBalance);
    public string CustomersText => _report is null ? string.Empty
        : Customers(_report.Rows.Count(r => r.Pending > 0), "owes", "owe") + " money · " +
          Customers(_report.Rows.Count(r => r.Ageing.Overdue > 0), "is", "are") + " overdue";

    private static string Customers(int count, string one, string many) =>
        count == 1 ? $"1 customer {one}" : $"{count} customers {many}";

    public override async Task InitializeAsync()
    {
        _report = await _reports.GetAsync(DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime));
        ShowRows();
        foreach (var name in new[] { nameof(IsEmpty), nameof(AsOfText), nameof(TotalPendingText), nameof(TotalOverdueText),
                                     nameof(TotalAdvanceText), nameof(TotalBalanceText), nameof(CustomersText) })
        {
            OnPropertyChanged(name);
        }

        CommandManager.InvalidateRequerySuggested();
    }

    private void ShowRows() =>
        Rows = _report is null
            ? []
            : _report.Rows.Where(r => !OverdueOnly || r.Ageing.Overdue > 0).Select(r => new OutstandingRowView(r)).ToList();

    private async Task PrintAsync()
    {
        Mouse.OverrideCursor = Cursors.Wait;
        DocumentPrintout document;
        try
        {
            document = await _printing.OutstandingAsync(_report!);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        _documents.Preview(document);
    }

    private void Export() =>
        _documents.SaveCsv(OutstandingReportService.ToCsv(_report!),
            $"Outstanding-{_report!.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv");
}
