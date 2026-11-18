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
using SareeBilling.Domain.Enums;

namespace SareeBilling.Desktop.ViewModels;

/// <summary>A customer in the list, with the current balance (+ = owes us).</summary>
public sealed record CustomerListItem(CustomerDto Customer, decimal Balance)
{
    public string Name => Customer.Name;
    public string City => Customer.City;
    public string State => Customer.State;
    public string? Gstin => Customer.Gstin;
    public bool IsActive => Customer.IsActive;
    public string BalanceText => Balance == 0 ? string.Empty : LedgerFormat.Balance(Balance);
}

public sealed record OpeningSideOption(BalanceSide Side, string Label);

public sealed record OpeningBillRowView(OpeningBillDto Bill)
{
    public string Title => Bill.Side == BalanceSide.Debit ? $"Old bill {Bill.BillNumber}" : $"Advance · {Bill.BillNumber}";
    public string DateText => Bill.BillDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    public string AmountText => IndianFormat.Amount(Bill.Amount) + (Bill.Side == BalanceSide.Debit ? " Dr" : " Cr");
    public string UsedText => Bill.Used == 0 ? string.Empty
        : Bill.Side == BalanceSide.Debit ? $"{IndianFormat.Amount(Bill.Used)} received" : $"{IndianFormat.Amount(Bill.Used)} used";
    public bool CanRemove => Bill.Used == 0;
}

public sealed class CustomersViewModel : PageViewModel
{
    private readonly ICustomerService _service;
    private readonly IReceiptService _receipts;
    private readonly IOpeningBillService _openingBills;
    private readonly IDialogService _dialogs;
    private readonly ErrorLog _errorLog;

    private string _searchText = string.Empty;
    private bool _showInactive;
    private CustomerListItem? _selectedCustomer;
    private string? _errorMessage;
    private string? _successMessage;
    private int _searchVersion;

    private string _newOpeningNumber = string.Empty;
    private DateTime? _newOpeningDate;
    private OpeningSideOption _newOpeningSide;
    private string _newOpeningAmount = string.Empty;
    private string? _openingError;

    public CustomersViewModel(
        ICustomerService service,
        IReceiptService receipts,
        IOpeningBillService openingBills,
        IDialogService dialogs,
        INavigationService navigation,
        ErrorLog errorLog)
    {
        _service = service;
        _receipts = receipts;
        _openingBills = openingBills;
        _dialogs = dialogs;
        _errorLog = errorLog;

        OpeningSides =
        [
            new(BalanceSide.Debit, "Customer owes (unpaid bill)"),
            new(BalanceSide.Credit, "Customer paid in advance")
        ];
        _newOpeningSide = OpeningSides[0];

        NewCommand = new RelayCommand(StartNew);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => !Editor.IsNew);
        AddOpeningBillCommand = new AsyncRelayCommand(AddOpeningBillAsync, () => !Editor.IsNew);
        RemoveOpeningBillCommand = new AsyncRelayCommand<OpeningBillRowView>(RemoveOpeningBillAsync);
        OpenLedgerCommand = new RelayCommand(() => navigation.NavigateTo(AppPage.CustomerLedger, Editor.Id), () => !Editor.IsNew);
        ReceivePaymentCommand = new RelayCommand(() => navigation.NavigateTo(AppPage.ReceivePayment, Editor.Id), () => !Editor.IsNew);
    }

    public override string Title => "Customers";

    public ObservableCollection<CustomerListItem> Customers { get; } = [];

    public CustomerEditorViewModel Editor { get; } = new();

    public ObservableCollection<OpeningBillRowView> OpeningBills { get; } = [];

    public IReadOnlyList<OpeningSideOption> OpeningSides { get; }

    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand AddOpeningBillCommand { get; }
    public ICommand RemoveOpeningBillCommand { get; }
    public ICommand OpenLedgerCommand { get; }
    public ICommand ReceivePaymentCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RefreshInBackground();
            }
        }
    }

    public bool ShowInactive
    {
        get => _showInactive;
        set
        {
            if (SetProperty(ref _showInactive, value))
            {
                RefreshInBackground();
            }
        }
    }

    public CustomerListItem? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value) && value is not null)
            {
                Editor.Load(value.Customer);
                ClearMessages();
                LoadOpeningBillsInBackground();
            }
        }
    }

    public string ResultSummary => Customers.Count == 1 ? "1 customer" : $"{Customers.Count} customers";

    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public string? SuccessMessage { get => _successMessage; private set => SetProperty(ref _successMessage, value); }

    public string NewOpeningNumber { get => _newOpeningNumber; set => SetProperty(ref _newOpeningNumber, value); }
    public DateTime? NewOpeningDate { get => _newOpeningDate; set => SetProperty(ref _newOpeningDate, value); }
    public OpeningSideOption NewOpeningSide { get => _newOpeningSide; set => SetProperty(ref _newOpeningSide, value); }
    public string NewOpeningAmount { get => _newOpeningAmount; set => SetProperty(ref _newOpeningAmount, value); }
    public string? OpeningError { get => _openingError; private set => SetProperty(ref _openingError, value); }

    public string OpeningTotalText
    {
        get
        {
            var total = OpeningBills.Sum(b => b.Bill.Side == BalanceSide.Debit ? b.Bill.Amount : -b.Bill.Amount);
            return OpeningBills.Count == 0 ? "No old bills entered." : "Opening balance: " + LedgerFormat.Balance(total);
        }
    }

    public override Task InitializeAsync() => RefreshAsync();

    private async Task RefreshAsync(int? selectId = null)
    {
        var version = ++_searchVersion;
        var results = await _service.SearchAsync(SearchText, ShowInactive);
        var balances = await _receipts.GetBalancesAsync();

        // Typing quickly starts several searches; only the newest one updates the list.
        if (version != _searchVersion)
        {
            return;
        }

        Customers.Clear();
        foreach (var customer in results)
        {
            Customers.Add(new CustomerListItem(customer, balances.GetValueOrDefault(customer.Id)));
        }

        OnPropertyChanged(nameof(ResultSummary));

        if (selectId is not null)
        {
            _selectedCustomer = Customers.FirstOrDefault(c => c.Customer.Id == selectId);
            OnPropertyChanged(nameof(SelectedCustomer));
        }
    }

    private async void RefreshInBackground()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _errorLog.Write("Customer search failed", ex);
            _dialogs.ShowError("Could not search customers.\n\n" + ex.Message);
        }
    }

    private async void LoadOpeningBillsInBackground()
    {
        try
        {
            await LoadOpeningBillsAsync();
        }
        catch (Exception ex)
        {
            _errorLog.Write("Loading opening bills failed", ex);
            _dialogs.ShowError("Could not load the customer's old bills.\n\n" + ex.Message);
        }
    }

    private async Task LoadOpeningBillsAsync()
    {
        OpeningError = null;
        OpeningBills.Clear();
        if (!Editor.IsNew)
        {
            foreach (var bill in await _openingBills.GetAsync(Editor.Id))
            {
                OpeningBills.Add(new OpeningBillRowView(bill));
            }
        }

        OnPropertyChanged(nameof(OpeningTotalText));
    }

    private void StartNew()
    {
        SelectedCustomer = null;
        Editor.Clear();
        ClearMessages();
        OpeningBills.Clear();
        OnPropertyChanged(nameof(OpeningTotalText));
    }

    private async Task SaveAsync()
    {
        ClearMessages();
        var wasNew = Editor.IsNew;

        try
        {
            var id = await _service.SaveAsync(Editor.ToDto());
            var saved = await _service.GetAsync(id);
            if (saved is not null)
            {
                Editor.Load(saved);
            }

            await RefreshAsync(selectId: id);
            await LoadOpeningBillsAsync();
            SuccessMessage = wasNew
                ? "Customer added. You can now enter any old unpaid bills below."
                : "Changes saved.";
        }
        catch (ValidationException ex)
        {
            ErrorMessage = "Please correct the following:\n• " + string.Join("\n• ", ex.Errors);
        }
    }

    private async Task DeleteAsync()
    {
        ClearMessages();
        var id = Editor.Id;
        var name = Editor.Name;

        var hasInvoices = await _service.HasInvoicesAsync(id);
        var question = hasInvoices
            ? $"{name} already has invoices, receipts or old bills, so it cannot be deleted.\n\n" +
              "Do you want to hide this customer instead? Everything is kept, " +
              "and you can show it again with \"Show inactive\"."
            : $"Delete customer {name}?\n\nThis cannot be undone.";

        if (!_dialogs.Confirm(question, hasInvoices ? "Hide customer" : "Delete customer"))
        {
            return;
        }

        var result = await _service.RemoveAsync(id);
        Editor.Clear();
        _selectedCustomer = null;
        OnPropertyChanged(nameof(SelectedCustomer));
        OpeningBills.Clear();
        await RefreshAsync();

        SuccessMessage = result == CustomerRemovalResult.Deleted
            ? $"{name} deleted."
            : $"{name} hidden. Tick \"Show inactive\" to see it.";
    }

    private async Task AddOpeningBillAsync()
    {
        OpeningError = null;
        var amount = ReceivePaymentViewModel.ParseAmount(NewOpeningAmount);
        if (amount is null)
        {
            OpeningError = $"\"{NewOpeningAmount}\" is not an amount.";
            return;
        }

        try
        {
            await _openingBills.AddAsync(new OpeningBillDto
            {
                CustomerId = Editor.Id,
                BillNumber = NewOpeningNumber,
                BillDate = NewOpeningDate is { } d ? DateOnly.FromDateTime(d) : default,
                Side = NewOpeningSide.Side,
                Amount = amount.Value
            });

            NewOpeningNumber = string.Empty;
            NewOpeningAmount = string.Empty;
            await LoadOpeningBillsAsync();
            await RefreshAsync(selectId: Editor.Id);
        }
        catch (ValidationException ex)
        {
            OpeningError = string.Join("\n", ex.Errors);
        }
    }

    private async Task RemoveOpeningBillAsync(OpeningBillRowView row)
    {
        if (!_dialogs.Confirm($"Remove {row.Title} ({row.AmountText}) from {Editor.Name}'s opening balance?", "Remove old bill"))
        {
            return;
        }

        try
        {
            await _openingBills.RemoveAsync(row.Bill.Id);
            await LoadOpeningBillsAsync();
            await RefreshAsync(selectId: Editor.Id);
        }
        catch (ValidationException ex)
        {
            OpeningError = string.Join("\n", ex.Errors);
        }
    }

    private void ClearMessages()
    {
        ErrorMessage = null;
        SuccessMessage = null;
    }
}
