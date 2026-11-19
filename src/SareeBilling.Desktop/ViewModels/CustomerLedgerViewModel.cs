using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SareeBilling.Application.Common;
using SareeBilling.Application.Customers;
using SareeBilling.Application.Receivables;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Navigation;
using SareeBilling.Desktop.Services;
using SareeBilling.Domain.Common;

namespace SareeBilling.Desktop.ViewModels;

public sealed record LedgerFilterOption(string Label, LedgerEntryType[]? Types);

public sealed record LedgerRowView(LedgerEntry Entry)
{
    public string DateText => LedgerFormat.Date(Entry.Date);
    public string TypeText => LedgerFormat.TypeLabel(Entry.Type);
    public string Reference => Entry.Reference;
    public string Particulars => Entry.Particulars;
    public string DebitText => Entry.Debit == 0 ? string.Empty : IndianFormat.Amount(Entry.Debit);
    public string CreditText => Entry.Credit == 0 ? string.Empty : IndianFormat.Amount(Entry.Credit);
    public string BalanceText => LedgerFormat.Balance(Entry.Balance);
    public bool CanOpen => Entry.Type is LedgerEntryType.SalesInvoice or LedgerEntryType.Receipt or LedgerEntryType.CreditNote;
}

public sealed class CustomerLedgerViewModel : PageViewModel
{
    private readonly ICustomerLedgerService _ledgers;
    private readonly ICustomerService _customers;
    private readonly IAccountsPrintService _printing;
    private readonly IDocumentActions _documents;
    private readonly IInvoiceActions _invoiceActions;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;

    private CustomerDto? _selectedCustomer;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private LedgerFilterOption _selectedFilter;
    private CustomerLedger? _ledger;
    private LedgerRowView? _selectedRow;
    private string? _errorMessage;
    private int? _pendingCustomerId;
    private int _loadVersion;

    public CustomerLedgerViewModel(
        ICustomerLedgerService ledgers,
        ICustomerService customers,
        IAccountsPrintService printing,
        IDocumentActions documents,
        IInvoiceActions invoiceActions,
        INavigationService navigation,
        IDialogService dialogs,
        TimeProvider timeProvider)
    {
        _ledgers = ledgers;
        _customers = customers;
        _printing = printing;
        _documents = documents;
        _invoiceActions = invoiceActions;
        _navigation = navigation;
        _dialogs = dialogs;

        // Default period: this financial year (1 April to today).
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var fyStart = new DateOnly(today.Month >= 4 ? today.Year : today.Year - 1, 4, 1);
        _fromDate = fyStart.ToDateTime(TimeOnly.MinValue);
        _toDate = today.ToDateTime(TimeOnly.MinValue);

        Filters =
        [
            new("All entries", null),
            new("Sales invoices", [LedgerEntryType.SalesInvoice]),
            new("Receipts", [LedgerEntryType.Receipt]),
            new("Credit notes", [LedgerEntryType.CreditNote]),
            new("Opening entries", [LedgerEntryType.OpeningBill, LedgerEntryType.OpeningAdvance])
        ];
        _selectedFilter = Filters[0];

        OpenEntryCommand = new AsyncRelayCommand(OpenEntryAsync, () => SelectedRow is { CanOpen: true });
        PrintCommand = new AsyncRelayCommand(PrintAsync, () => _ledger is not null);
        ExportCommand = new RelayCommand(Export, () => _ledger is not null);
        ReceivePaymentCommand = new RelayCommand(
            () => _navigation.NavigateTo(AppPage.ReceivePayment, SelectedCustomer!.Id), () => SelectedCustomer is not null);
    }

    public override string Title => "Customer Ledger";

    public ObservableCollection<CustomerDto> Customers { get; } = [];

    public IReadOnlyList<LedgerFilterOption> Filters { get; }

    public ObservableCollection<LedgerRowView> Rows { get; } = [];

    public ICommand OpenEntryCommand { get; }
    public ICommand PrintCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ReceivePaymentCommand { get; }

    public CustomerDto? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value))
            {
                ReloadInBackground();
            }
        }
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            if (SetProperty(ref _fromDate, value))
            {
                ReloadInBackground();
            }
        }
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set
        {
            if (SetProperty(ref _toDate, value))
            {
                ReloadInBackground();
            }
        }
    }

    public LedgerFilterOption SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                ShowRows();
            }
        }
    }

    public LedgerRowView? SelectedRow { get => _selectedRow; set => SetProperty(ref _selectedRow, value); }

    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

    public bool HasLedger => _ledger is not null;

    public bool HasNoCustomer => SelectedCustomer is null;

    public string PartyText => _ledger is null ? string.Empty : $"{_ledger.CustomerName} · {_ledger.City} · credit {_ledger.CreditDays} days";
    public string OpeningText => _ledger is null ? string.Empty : LedgerFormat.Balance(_ledger.OpeningBalance);
    public string OpeningDateText => _ledger is null ? string.Empty : LedgerFormat.Date(_ledger.From);
    public string TotalDebitText => _ledger is null ? string.Empty : IndianFormat.Amount(_ledger.TotalDebit);
    public string TotalCreditText => _ledger is null ? string.Empty : IndianFormat.Amount(_ledger.TotalCredit);
    public string ClosingText => _ledger is null ? string.Empty : LedgerFormat.Balance(_ledger.ClosingBalance);
    public string ClosingLabel => _ledger is null ? string.Empty
        : _ledger.ClosingBalance > 0 ? "Balance to receive" : _ledger.ClosingBalance < 0 ? "Advance held" : "Balance";
    public bool IsFiltered => SelectedFilter.Types is not null;

    public override void ReceiveParameter(object parameter)
    {
        if (parameter is int customerId)
        {
            _pendingCustomerId = customerId;
        }
    }

    public override async Task InitializeAsync()
    {
        foreach (var customer in await _customers.SearchAsync(null, includeInactive: true))
        {
            Customers.Add(customer);
        }

        if (_pendingCustomerId is { } id)
        {
            SelectedCustomer = Customers.FirstOrDefault(c => c.Id == id);
        }
    }

    private async void ReloadInBackground()
    {
        try
        {
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("Could not load the ledger.\n\n" + ex.Message);
        }
    }

    private async Task ReloadAsync()
    {
        var version = ++_loadVersion;
        ErrorMessage = null;
        OnPropertyChanged(nameof(HasNoCustomer));

        CustomerLedger? ledger = null;
        if (SelectedCustomer is not null)
        {
            if (FromDate is null || ToDate is null)
            {
                ErrorMessage = "Choose both a From and a To date.";
            }
            else
            {
                try
                {
                    ledger = await _ledgers.GetAsync(SelectedCustomer.Id, DateOnly.FromDateTime(FromDate.Value), DateOnly.FromDateTime(ToDate.Value));
                }
                catch (ValidationException ex)
                {
                    ErrorMessage = string.Join("\n", ex.Errors);
                }
            }
        }

        if (version != _loadVersion)
        {
            return;
        }

        _ledger = ledger;
        ShowRows();
        foreach (var name in new[]
                 {
                     nameof(HasLedger), nameof(PartyText), nameof(OpeningText), nameof(OpeningDateText), nameof(TotalDebitText),
                     nameof(TotalCreditText), nameof(ClosingText), nameof(ClosingLabel)
                 })
        {
            OnPropertyChanged(name);
        }

        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>The type filter only hides rows; balances stay the true running balance.</summary>
    private void ShowRows()
    {
        Rows.Clear();
        if (_ledger is not null)
        {
            foreach (var entry in _ledger.Entries.Where(e => SelectedFilter.Types is null || SelectedFilter.Types.Contains(e.Type)))
            {
                Rows.Add(new LedgerRowView(entry));
            }
        }

        OnPropertyChanged(nameof(IsFiltered));
    }

    private async Task OpenEntryAsync()
    {
        var entry = SelectedRow!.Entry;
        if (entry.Type == LedgerEntryType.SalesInvoice)
        {
            await _invoiceActions.PreviewAsync(entry.SourceId);
        }
        else if (entry.Type == LedgerEntryType.Receipt)
        {
            _documents.Preview(await WithWaitCursor(() => _printing.ReceiptAsync(entry.SourceId)));
        }
        else if (entry.Type == LedgerEntryType.CreditNote)
        {
            _documents.Preview(await WithWaitCursor(() => _printing.CreditNoteAsync(entry.SourceId)));
        }
    }

    private async Task PrintAsync() => _documents.Preview(await WithWaitCursor(() => _printing.StatementAsync(_ledger!)));

    private void Export() =>
        _documents.SaveCsv(LedgerFormat.ToCsv(_ledger!),
            AccountsPrintService.SafeFileName($"Ledger-{_ledger!.CustomerName}-{_ledger.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv"));

    private static async Task<T> WithWaitCursor<T>(Func<Task<T>> work)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            return await work();
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
}
