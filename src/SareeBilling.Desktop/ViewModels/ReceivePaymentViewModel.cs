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

public sealed record PaymentModeOption(PaymentMode Mode, string Label);

/// <summary>One unpaid bill in the "settle against" table; the user types how much of it this money pays.</summary>
public sealed class SettleBillRow : ObservableObject
{
    private string _amountText = string.Empty;

    public SettleBillRow(AccountBill bill, DateOnly today)
    {
        Bill = bill;
        var late = bill.DaysOverdue(today);
        DueText = late > 0
            ? $"{bill.DueDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)} · {late} {(late == 1 ? "day" : "days")} late"
            : bill.DueDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        IsOverdue = late > 0;
    }

    public AccountBill Bill { get; }

    public string Number => Bill.Number;

    public string DateText => Bill.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    public string DueText { get; }

    public bool IsOverdue { get; }

    public string TotalText => IndianFormat.Amount(Bill.Total);

    public string PendingText => IndianFormat.Amount(Bill.Pending);

    public event EventHandler? AmountChanged;

    public string AmountText
    {
        get => _amountText;
        set
        {
            if (SetProperty(ref _amountText, value))
            {
                AmountChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>The typed amount; 0 when empty; null when it is not a number.</summary>
    public decimal? Amount => ReceivePaymentViewModel.ParseAmount(AmountText);
}

public sealed class ReceivePaymentViewModel : PageViewModel
{
    private readonly IReceiptService _receipts;
    private readonly ICustomerService _customers;
    private readonly IAccountsPrintService _printing;
    private readonly IDocumentActions _documents;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly TimeProvider _timeProvider;

    private CustomerDto? _selectedCustomer;
    private CustomerAccount? _account;
    private DateTime? _receiptDate;
    private PaymentModeOption _selectedMode;
    private string _amountText = string.Empty;
    private string _reference = string.Empty;
    private string _narration = string.Empty;
    private bool _isAdjustingAdvance;
    private string? _errorMessage;
    private string? _successMessage;
    private int? _lastSavedReceiptId;
    private int? _pendingCustomerId;

    public ReceivePaymentViewModel(
        IReceiptService receipts,
        ICustomerService customers,
        IAccountsPrintService printing,
        IDocumentActions documents,
        IDialogService dialogs,
        INavigationService navigation,
        TimeProvider timeProvider)
    {
        _receipts = receipts;
        _customers = customers;
        _printing = printing;
        _documents = documents;
        _dialogs = dialogs;
        _navigation = navigation;
        _timeProvider = timeProvider;

        Modes =
        [
            new(PaymentMode.BankTransfer, "Bank transfer / UPI"),
            new(PaymentMode.Cash, "Cash"),
            new(PaymentMode.Cheque, "Cheque")
        ];
        _selectedMode = Modes[0];
        _receiptDate = Today.ToDateTime(TimeOnly.MinValue);

        AutoFillCommand = new RelayCommand(AutoFill, () => _account is not null);
        ClearAllocationsCommand = new RelayCommand(() => SetAmounts([]), () => _account is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => _account is not null);
        StartAdjustAdvanceCommand = new RelayCommand(StartAdjustAdvance, () => CanAdjustAdvance);
        CancelAdjustAdvanceCommand = new RelayCommand(() => SetAdjusting(false));
        PrintLastReceiptCommand = new AsyncRelayCommand(PrintLastReceiptAsync, () => _lastSavedReceiptId is not null);
        OpenLedgerCommand = new RelayCommand(
            () => _navigation.NavigateTo(AppPage.CustomerLedger, SelectedCustomer!.Id), () => SelectedCustomer is not null);
    }

    public override string Title => IsAdjustingAdvance ? "Adjust Advance" : "Receive Payment";

    public ObservableCollection<CustomerDto> Customers { get; } = [];

    public IReadOnlyList<PaymentModeOption> Modes { get; }

    public ObservableCollection<SettleBillRow> Bills { get; } = [];

    public ICommand AutoFillCommand { get; }
    public ICommand ClearAllocationsCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand StartAdjustAdvanceCommand { get; }
    public ICommand CancelAdjustAdvanceCommand { get; }
    public ICommand PrintLastReceiptCommand { get; }
    public ICommand OpenLedgerCommand { get; }

    public CustomerDto? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value))
            {
                LoadAccountInBackground();
            }
        }
    }

    public DateTime? ReceiptDate { get => _receiptDate; set => SetProperty(ref _receiptDate, value); }

    public PaymentModeOption SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                OnPropertyChanged(nameof(ReferenceLabel));
            }
        }
    }

    public string ReferenceLabel => SelectedMode.Mode switch
    {
        PaymentMode.Cheque => "Cheque no. *",
        PaymentMode.Cash => "Reference (optional)",
        _ => "UTR / UPI ref. (optional)"
    };

    public string AmountText
    {
        get => _amountText;
        set
        {
            if (SetProperty(ref _amountText, value))
            {
                UpdateTotals();
            }
        }
    }

    public string Reference { get => _reference; set => SetProperty(ref _reference, value); }

    public string Narration { get => _narration; set => SetProperty(ref _narration, value); }

    public bool IsAdjustingAdvance
    {
        get => _isAdjustingAdvance;
        private set
        {
            if (SetProperty(ref _isAdjustingAdvance, value))
            {
                OnPropertyChanged(nameof(IsReceiving));
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(SaveButtonText));
            }
        }
    }

    public bool IsReceiving => !IsAdjustingAdvance;

    public string SaveButtonText => IsAdjustingAdvance ? "Save adjustment" : "Save receipt";

    public bool HasAccount => _account is not null;

    public bool HasNoOpenBills => _account is not null && Bills.Count == 0;

    public bool HasOpenBills => Bills.Count > 0;

    public string AccountSummary => _account is null
        ? string.Empty
        : $"Pending bills {IndianFormat.Amount(_account.Pending)} · Advance {IndianFormat.Amount(_account.Advance)} · " +
          $"Balance {LedgerFormat.Balance(_account.Balance)} · Credit {_account.CreditDays} days";

    public bool CanAdjustAdvance => _account is { Advance: > 0 } && _account.OpenBills.Count > 0 && !IsAdjustingAdvance;

    public string AdvanceNotice => _account is null
        ? string.Empty
        : $"{_account.CustomerName} has an unused advance of ₹ {IndianFormat.Amount(_account.Advance)}.";

    public string SettledText { get; private set; } = "0.00";
    public string AdvanceText { get; private set; } = "0.00";
    public string BalanceAfterText { get; private set; } = string.Empty;
    public bool IsOverAllocated { get; private set; }

    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public string? SuccessMessage { get => _successMessage; private set => SetProperty(ref _successMessage, value); }

    public bool CanPrintLastReceipt => _lastSavedReceiptId is not null;

    /// <summary>Money typed but not saved; app updates and page changes ask first.</summary>
    public override bool HasUnsavedWork =>
        _account is not null && (ParseAmount(AmountText) is > 0 || Bills.Any(b => b.Amount is > 0));

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

    public override void ReceiveParameter(object parameter)
    {
        if (parameter is int customerId)
        {
            _pendingCustomerId = customerId;
        }
    }

    public override async Task InitializeAsync()
    {
        foreach (var customer in await _customers.SearchAsync(null, includeInactive: false))
        {
            Customers.Add(customer);
        }

        if (_pendingCustomerId is { } id)
        {
            SelectedCustomer = Customers.FirstOrDefault(c => c.Id == id);
        }
    }

    public override bool ConfirmLeave() =>
        !HasUnsavedWork || _dialogs.Confirm("The payment you were entering has not been saved. Leave without saving?", "Leave Receive Payment");

    /// <summary>"50,000.00", "50000" or "₹ 50,000" → 50000; empty → 0; anything else → null.</summary>
    public static decimal? ParseAmount(string? text)
    {
        var clean = (text ?? string.Empty).Replace("₹", string.Empty).Trim();
        if (clean.Length == 0)
        {
            return 0m;
        }

        return decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private async void LoadAccountInBackground()
    {
        try
        {
            await LoadAccountAsync();
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("Could not load the customer's bills.\n\n" + ex.Message);
        }
    }

    private async Task LoadAccountAsync(bool keepMessages = false)
    {
        if (!keepMessages)
        {
            ErrorMessage = null;
            SuccessMessage = null;
        }

        IsAdjustingAdvance = false;
        _account = SelectedCustomer is null ? null : await _receipts.GetAccountAsync(SelectedCustomer.Id);

        foreach (var row in Bills)
        {
            row.AmountChanged -= OnRowAmountChanged;
        }

        Bills.Clear();
        if (_account is not null)
        {
            foreach (var bill in _account.OpenBills)
            {
                var row = new SettleBillRow(bill, Today);
                row.AmountChanged += OnRowAmountChanged;
                Bills.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasAccount));
        OnPropertyChanged(nameof(HasNoOpenBills));
        OnPropertyChanged(nameof(HasOpenBills));
        OnPropertyChanged(nameof(AccountSummary));
        OnPropertyChanged(nameof(CanAdjustAdvance));
        OnPropertyChanged(nameof(AdvanceNotice));
        UpdateTotals();
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnRowAmountChanged(object? sender, EventArgs e) => UpdateTotals();

    private void AutoFill()
    {
        ErrorMessage = null;
        var available = IsAdjustingAdvance ? _account!.Advance : ParseAmount(AmountText);
        if (available is not > 0)
        {
            ErrorMessage = "Enter the amount received first.";
            return;
        }

        SetAmounts(AllocationRules.AutoFill(_account!, available.Value));
    }

    private void SetAmounts(IReadOnlyList<AllocationLine> lines)
    {
        foreach (var row in Bills)
        {
            var line = lines.FirstOrDefault(l => l.Bill == row.Bill.Ref);
            row.AmountText = line is null ? string.Empty : IndianFormat.Amount(line.Amount);
        }
    }

    private void StartAdjustAdvance()
    {
        SetAdjusting(true);
        AutoFill();
    }

    private void SetAdjusting(bool adjusting)
    {
        IsAdjustingAdvance = adjusting;
        ErrorMessage = null;
        SuccessMessage = null;
        SetAmounts([]);
        OnPropertyChanged(nameof(CanAdjustAdvance));
        UpdateTotals();
    }

    private void UpdateTotals()
    {
        var settled = Bills.Sum(b => b.Amount ?? 0m);
        var available = IsAdjustingAdvance ? _account?.Advance ?? 0m : ParseAmount(AmountText) ?? 0m;

        SettledText = IndianFormat.Amount(settled);
        AdvanceText = IndianFormat.Amount(Math.Max(0, available - settled));
        IsOverAllocated = settled > available;
        BalanceAfterText = _account is null || IsAdjustingAdvance
            ? string.Empty
            : "Balance after this receipt: " + LedgerFormat.Balance(_account.Balance - available);

        OnPropertyChanged(nameof(SettledText));
        OnPropertyChanged(nameof(AdvanceText));
        OnPropertyChanged(nameof(IsOverAllocated));
        OnPropertyChanged(nameof(BalanceAfterText));
    }

    private async Task SaveAsync()
    {
        ErrorMessage = null;
        SuccessMessage = null;

        var badRow = Bills.FirstOrDefault(b => b.Amount is null);
        if (badRow is not null)
        {
            ErrorMessage = $"Bill {badRow.Number}: \"{badRow.AmountText}\" is not an amount.";
            return;
        }

        var lines = Bills.Where(b => b.Amount is > 0).Select(b => new AllocationLine(b.Bill.Ref, b.Amount!.Value)).ToList();

        try
        {
            if (IsAdjustingAdvance)
            {
                await SaveAdjustmentAsync(lines);
            }
            else
            {
                await SaveReceiptAsync(lines);
            }
        }
        catch (ValidationException ex)
        {
            ErrorMessage = "Please correct the following:\n• " + string.Join("\n• ", ex.Errors);
        }
    }

    private async Task SaveReceiptAsync(List<AllocationLine> lines)
    {
        var amount = ParseAmount(AmountText);
        if (amount is null)
        {
            ErrorMessage = $"\"{AmountText}\" is not an amount.";
            return;
        }

        var request = new CreateReceiptRequest
        {
            CustomerId = SelectedCustomer!.Id,
            ReceiptDate = ReceiptDate is { } d ? DateOnly.FromDateTime(d) : default,
            Amount = amount.Value,
            Mode = SelectedMode.Mode,
            Reference = Reference,
            Narration = Narration,
            Allocations = lines
        };

        var settled = lines.Sum(l => l.Amount);
        var settles = lines.Count == 0
            ? "No bills (all kept as advance)"
            : string.Join(", ", lines.Select(l => $"{Bills.First(b => b.Bill.Ref == l.Bill).Number}: {IndianFormat.Amount(l.Amount)}"));
        var question =
            $"Save this receipt?\n\n" +
            $"From: {SelectedCustomer.Name}\n" +
            $"Amount: ₹ {IndianFormat.Amount(amount.Value)} ({SelectedMode.Label})\n" +
            $"Settles: {settles}\n" +
            (amount.Value > settled ? $"Kept as advance: ₹ {IndianFormat.Amount(amount.Value - settled)}\n" : string.Empty) +
            "\nA saved receipt cannot be edited; it can only be cancelled with a reason.";
        if (!_dialogs.Confirm(question, "Save receipt"))
        {
            return;
        }

        var created = await _receipts.CreateAsync(request);
        _lastSavedReceiptId = created.Id;
        OnPropertyChanged(nameof(CanPrintLastReceipt));

        AmountText = string.Empty;
        Reference = string.Empty;
        Narration = string.Empty;
        await LoadAccountAsync(keepMessages: true);
        SuccessMessage = $"Receipt {created.ReceiptNumber} saved: ₹ {IndianFormat.Amount(created.Amount)}" +
                         (created.KeptAsAdvance > 0 ? $" (₹ {IndianFormat.Amount(created.KeptAsAdvance)} kept as advance)." : ".");
    }

    private async Task SaveAdjustmentAsync(List<AllocationLine> lines)
    {
        if (lines.Count == 0)
        {
            ErrorMessage = "Enter an amount against at least one bill.";
            return;
        }

        var total = lines.Sum(l => l.Amount);
        if (!_dialogs.Confirm(
                $"Use ₹ {IndianFormat.Amount(total)} of {SelectedCustomer!.Name}'s advance to settle " +
                $"{(lines.Count == 1 ? "1 bill" : $"{lines.Count} bills")}?",
                "Adjust advance"))
        {
            return;
        }

        await _receipts.AdjustAdvanceAsync(SelectedCustomer.Id, lines);
        await LoadAccountAsync(keepMessages: true);
        SuccessMessage = $"Advance of ₹ {IndianFormat.Amount(total)} adjusted against the bills.";
    }

    private async Task PrintLastReceiptAsync()
    {
        Mouse.OverrideCursor = Cursors.Wait;
        DocumentPrintout document;
        try
        {
            document = await _printing.ReceiptAsync(_lastSavedReceiptId!.Value);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        _documents.Preview(document);
    }
}
