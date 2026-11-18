using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SareeBilling.Application.Common;
using SareeBilling.Application.CompanyProfiles;
using SareeBilling.Application.Customers;
using SareeBilling.Application.Invoices;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Navigation;
using SareeBilling.Desktop.Services;
using SareeBilling.Domain.Common;
using SareeBilling.Domain.Enums;
using SareeBilling.Domain.Gst;

namespace SareeBilling.Desktop.ViewModels;

/// <summary>
/// The invoice entry screen. Totals shown here are a live preview using the same
/// GstCalculator as the save; the service recalculates and stores the official values.
/// </summary>
public sealed class CreateInvoiceViewModel : PageViewModel
{
    private const string DefaultUnit = "Qty";
    private const string DefaultGstRate = "5";

    private readonly IInvoiceService _invoices;
    private readonly ICustomerService _customers;
    private readonly ICompanyProfileService _companyProfiles;
    private readonly IDialogService _dialogs;
    private readonly IInvoiceActions _actions;
    private readonly TimeProvider _timeProvider;

    private CompanyProfileDto? _company;
    private GstInvoiceResult? _lastResult;
    private InvoiceLineViewModel? _editingLine;

    private bool _isLoaded;
    private bool _isCompanyMissing;
    private CustomerDto? _selectedCustomer;
    private DateTime? _invoiceDate;
    private string _nextNumberText = string.Empty;

    private string _buyerOrderNumber = string.Empty;
    private DateTime? _buyerOrderDate;
    private string _paymentTerms = string.Empty;
    private string _dispatchDocumentNumber = string.Empty;
    private string _dispatchedThrough = string.Empty;
    private string _destination = string.Empty;
    private string _termsOfDelivery = string.Empty;

    private string _entryDescription = string.Empty;
    private string _entryHsnCode = string.Empty;
    private string _entryQuantity = string.Empty;
    private string _entryUnit = DefaultUnit;
    private string _entryRate = string.Empty;
    private string _entryGstRate = DefaultGstRate;
    private string? _entryError;

    private string? _errorMessage;
    private CreatedInvoice? _savedInvoice;
    private bool _isSavedInvoiceCancelled;

    public CreateInvoiceViewModel(
        IInvoiceService invoices,
        ICustomerService customers,
        ICompanyProfileService companyProfiles,
        IDialogService dialogs,
        IInvoiceActions actions,
        INavigationService navigation,
        TimeProvider timeProvider)
    {
        _invoices = invoices;
        _customers = customers;
        _companyProfiles = companyProfiles;
        _dialogs = dialogs;
        _actions = actions;
        _timeProvider = timeProvider;

        AddLineCommand = new RelayCommand(AddOrUpdateLine);
        EditLineCommand = new RelayCommand<InvoiceLineViewModel>(EditLine);
        RemoveLineCommand = new RelayCommand<InvoiceLineViewModel>(RemoveLine);
        CancelLineEditCommand = new RelayCommand(CancelLineEdit, () => IsEditingLine);
        NewCustomerCommand = new AsyncRelayCommand(AddNewCustomerAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => _isLoaded && !IsCompanyMissing && !IsSaved);
        ClearCommand = new AsyncRelayCommand(ClearAsync, () => !IsSaved);
        StartNewInvoiceCommand = new AsyncRelayCommand(ResetAsync);
        CancelSavedInvoiceCommand = new AsyncRelayCommand(CancelSavedInvoiceAsync, () => IsSaved && !IsSavedInvoiceCancelled);
        PreviewSavedInvoiceCommand = new AsyncRelayCommand(() => _actions.PreviewAsync(SavedInvoice!.Id), () => IsSaved);
        PrintSavedInvoiceCommand = new AsyncRelayCommand(() => _actions.PrintAsync(SavedInvoice!.Id), () => IsSaved);
        SaveSavedInvoicePdfCommand = new AsyncRelayCommand(() => _actions.SavePdfAsync(SavedInvoice!.Id), () => IsSaved);
        OpenCompanySettingsCommand = new RelayCommand(() => navigation.NavigateTo(AppPage.CompanySettings));
    }

    /// <summary>Raised after an item is added so the view can put the cursor back in Description.</summary>
    public event EventHandler? LineAdded;

    public override string Title => "Sales Invoice";

    // ---------- Lists for drop-downs ----------
    public ObservableCollection<CustomerDto> Customers { get; } = [];
    public IReadOnlyList<string> Units => InvoiceItemRules.CommonUnits;
    public IReadOnlyList<string> GstRates { get; } =
        InvoiceItemRules.CommonGstRates.Select(IndianFormat.Percent).ToList();

    // ---------- Commands ----------
    public ICommand AddLineCommand { get; }
    public ICommand EditLineCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand CancelLineEditCommand { get; }
    public ICommand NewCustomerCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand StartNewInvoiceCommand { get; }
    public ICommand CancelSavedInvoiceCommand { get; }
    public ICommand PreviewSavedInvoiceCommand { get; }
    public ICommand PrintSavedInvoiceCommand { get; }
    public ICommand SaveSavedInvoicePdfCommand { get; }
    public ICommand OpenCompanySettingsCommand { get; }

    // ---------- Header ----------
    public bool IsCompanyMissing
    {
        get => _isCompanyMissing;
        private set
        {
            if (SetProperty(ref _isCompanyMissing, value))
            {
                OnPropertyChanged(nameof(CanEdit));
            }
        }
    }

    /// <summary>The form is disabled until the company profile exists.</summary>
    public bool CanEdit => !IsCompanyMissing;

    /// <summary>Latest date selectable in the calendar (no future-dated invoices).</summary>
    public DateTime MaxInvoiceDate => Today();

    public CustomerDto? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value))
            {
                OnPropertyChanged(nameof(CustomerDetails));
                OnPropertyChanged(nameof(TaxTypeText));
                Recalculate();
            }
        }
    }

    public string CustomerDetails
    {
        get
        {
            if (SelectedCustomer is not { } c)
            {
                return string.Empty;
            }

            var address = PostalAddress.Format(c.AddressLine1, c.AddressLine2, c.City, c.Pincode);
            return $"{address}{Environment.NewLine}{c.State} (State code {c.StateCode})" +
                   $"{Environment.NewLine}GSTIN: {c.Gstin ?? "Not registered"}";
        }
    }

    public string TaxTypeText
    {
        get
        {
            if (SelectedCustomer is null || _company is null)
            {
                return string.Empty;
            }

            return GstCalculator.DetermineTaxType(_company.StateCode, SelectedCustomer.StateCode) == TaxType.IntraState
                ? "Same state as you → CGST + SGST"
                : "Other state → IGST";
        }
    }

    public DateTime? InvoiceDate { get => _invoiceDate; set => SetProperty(ref _invoiceDate, value); }

    public string NextNumberText { get => _nextNumberText; private set => SetProperty(ref _nextNumberText, value); }

    public string BuyerOrderNumber { get => _buyerOrderNumber; set => SetProperty(ref _buyerOrderNumber, value); }
    public DateTime? BuyerOrderDate { get => _buyerOrderDate; set => SetProperty(ref _buyerOrderDate, value); }
    public string PaymentTerms { get => _paymentTerms; set => SetProperty(ref _paymentTerms, value); }
    public string DispatchDocumentNumber { get => _dispatchDocumentNumber; set => SetProperty(ref _dispatchDocumentNumber, value); }
    public string DispatchedThrough { get => _dispatchedThrough; set => SetProperty(ref _dispatchedThrough, value); }
    public string Destination { get => _destination; set => SetProperty(ref _destination, value); }
    public string TermsOfDelivery { get => _termsOfDelivery; set => SetProperty(ref _termsOfDelivery, value); }

    // ---------- Item entry row ----------
    public string EntryDescription { get => _entryDescription; set => SetProperty(ref _entryDescription, value); }
    public string EntryHsnCode { get => _entryHsnCode; set => SetProperty(ref _entryHsnCode, value); }
    public string EntryQuantity { get => _entryQuantity; set => SetProperty(ref _entryQuantity, value); }
    public string EntryUnit { get => _entryUnit; set => SetProperty(ref _entryUnit, value); }
    public string EntryRate { get => _entryRate; set => SetProperty(ref _entryRate, value); }
    public string EntryGstRate { get => _entryGstRate; set => SetProperty(ref _entryGstRate, value); }
    public string? EntryError { get => _entryError; private set => SetProperty(ref _entryError, value); }

    public bool IsEditingLine => _editingLine is not null;
    public string AddLineButtonText => IsEditingLine ? "Update item" : "Add item";

    public ObservableCollection<InvoiceLineViewModel> Lines { get; } = [];

    // ---------- Totals ----------
    public string TotalQuantityText => Lines.Count == 0 ? "0" : IndianFormat.Quantity(Lines.Sum(l => l.Input.Quantity));
    public string TaxableTotalText { get; private set; } = IndianFormat.Amount(0);
    public string CgstText { get; private set; } = string.Empty;
    public string SgstText { get; private set; } = string.Empty;
    public string IgstText { get; private set; } = string.Empty;
    public string RoundOffText { get; private set; } = string.Empty;
    public string GrandTotalText { get; private set; } = "—";
    public string AmountInWordsText { get; private set; } = string.Empty;
    public bool ShowCgstSgst => _lastResult?.TaxType == TaxType.IntraState;
    public bool ShowIgst => _lastResult?.TaxType == TaxType.InterState;

    // ---------- Messages and saved state ----------
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

    public CreatedInvoice? SavedInvoice
    {
        get => _savedInvoice;
        private set
        {
            if (SetProperty(ref _savedInvoice, value))
            {
                OnPropertyChanged(nameof(IsSaved));
                OnPropertyChanged(nameof(IsEntryVisible));
                OnPropertyChanged(nameof(SavedHeadline));
                OnPropertyChanged(nameof(SavedDetails));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsSaved => SavedInvoice is not null;

    /// <summary>After saving, the entry form is replaced by the confirmation panel.</summary>
    public bool IsEntryVisible => !IsSaved;

    public bool IsSavedInvoiceCancelled
    {
        get => _isSavedInvoiceCancelled;
        private set
        {
            if (SetProperty(ref _isSavedInvoiceCancelled, value))
            {
                OnPropertyChanged(nameof(SavedHeadline));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string SavedHeadline => SavedInvoice is null
        ? string.Empty
        : IsSavedInvoiceCancelled
            ? $"Invoice {SavedInvoice.InvoiceNumber} has been CANCELLED"
            : $"Invoice {SavedInvoice.InvoiceNumber} saved";

    public string SavedDetails => SavedInvoice is null
        ? string.Empty
        : $"{SelectedCustomer?.Name} • Grand total Rs. {IndianFormat.Amount(SavedInvoice.GrandTotal)}" +
          $"{Environment.NewLine}You can print it now, or later from Invoice History.";

    // ---------- Loading ----------
    public override async Task InitializeAsync()
    {
        await ResetAsync();
        _isLoaded = true;
        CommandManager.InvalidateRequerySuggested();
    }

    public override bool HasUnsavedWork =>
        !IsSaved && (Lines.Count > 0 || !string.IsNullOrWhiteSpace(EntryDescription));

    public override bool ConfirmLeave()
    {
        if (!HasUnsavedWork)
        {
            return true;
        }

        return _dialogs.Confirm(
            "This invoice has not been saved.\n\nDo you want to leave and discard it?",
            "Discard invoice?");
    }

    private async Task ResetAsync()
    {
        _company = await _companyProfiles.GetAsync();
        IsCompanyMissing = _company is null;

        await ReloadCustomersAsync(selectId: null);

        SavedInvoice = null;
        IsSavedInvoiceCancelled = false;
        ErrorMessage = null;

        BuyerOrderNumber = string.Empty;
        BuyerOrderDate = null;
        PaymentTerms = string.Empty;
        DispatchDocumentNumber = string.Empty;
        DispatchedThrough = string.Empty;
        Destination = string.Empty;
        TermsOfDelivery = string.Empty;

        Lines.Clear();
        _editingLine = null;
        ClearEntry(keepDefaults: false);
        Recalculate();

        _invoiceDate = Today();
        OnPropertyChanged(nameof(InvoiceDate));
        await RefreshNextNumberAsync();
    }

    private async Task ClearAsync()
    {
        if (Lines.Count > 0 && !_dialogs.Confirm("Clear this invoice and start again?", "Clear invoice"))
        {
            return;
        }

        await ResetAsync();
    }

    private async Task ReloadCustomersAsync(int? selectId)
    {
        var customers = await _customers.SearchAsync(null, includeInactive: false);
        Customers.Clear();
        foreach (var customer in customers)
        {
            Customers.Add(customer);
        }

        SelectedCustomer = selectId is null ? null : Customers.FirstOrDefault(c => c.Id == selectId);
    }

    /// <summary>Numbers never restart, so the preview does not depend on the invoice date.</summary>
    private async Task RefreshNextNumberAsync() =>
        NextNumberText = await _invoices.PreviewNextNumberAsync() ?? string.Empty;

    private DateTime Today() => _timeProvider.GetLocalNow().Date;

    // ---------- Items ----------
    private void AddOrUpdateLine()
    {
        EntryError = null;
        ErrorMessage = null;

        var problems = new List<string>();
        var quantity = ParseNumber(EntryQuantity, "Quantity", problems);
        var rate = ParseNumber(EntryRate, "Rate", problems);
        var gstRate = ParseNumber(EntryGstRate.Replace("%", string.Empty), "GST %", problems);

        if (problems.Count == 0)
        {
            var input = new InvoiceItemInput
            {
                Description = EntryDescription.Trim(),
                HsnCode = EntryHsnCode.Trim(),
                Quantity = quantity,
                Unit = EntryUnit.Trim(),
                Rate = rate,
                GstRate = gstRate
            };

            problems.AddRange(InvoiceItemRules.Validate(input));
            if (problems.Count == 0 && _editingLine is null && Lines.Count >= InvoiceItemRules.MaxItemsPerInvoice)
            {
                problems.Add($"An invoice can have at most {InvoiceItemRules.MaxItemsPerInvoice} items.");
            }

            if (problems.Count == 0)
            {
                var line = new InvoiceLineViewModel(input);
                if (_editingLine is not null)
                {
                    Lines[Lines.IndexOf(_editingLine)] = line;
                    SetEditingLine(null);
                }
                else
                {
                    Lines.Add(line);
                }

                ClearEntry(keepDefaults: true);
                Recalculate();
                LineAdded?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        EntryError = string.Join("  ", problems);
    }

    private void EditLine(InvoiceLineViewModel line)
    {
        SetEditingLine(line);
        EntryDescription = line.Input.Description;
        EntryHsnCode = line.Input.HsnCode;
        EntryQuantity = line.Input.Quantity.ToString(CultureInfo.CurrentCulture);
        EntryUnit = line.Input.Unit;
        EntryRate = line.Input.Rate.ToString(CultureInfo.CurrentCulture);
        EntryGstRate = IndianFormat.Percent(line.Input.GstRate);
        EntryError = null;
    }

    private void CancelLineEdit()
    {
        SetEditingLine(null);
        ClearEntry(keepDefaults: true);
    }

    private void RemoveLine(InvoiceLineViewModel line)
    {
        if (!_dialogs.Confirm($"Remove item {line.LineNumber}: {line.Description}?", "Remove item"))
        {
            return;
        }

        if (ReferenceEquals(line, _editingLine))
        {
            CancelLineEdit();
        }

        Lines.Remove(line);
        Recalculate();
    }

    private void SetEditingLine(InvoiceLineViewModel? line)
    {
        _editingLine = line;
        OnPropertyChanged(nameof(IsEditingLine));
        OnPropertyChanged(nameof(AddLineButtonText));
    }

    /// <param name="keepDefaults">Keep HSN, unit and GST % so the next similar saree is quicker to enter.</param>
    private void ClearEntry(bool keepDefaults)
    {
        EntryDescription = string.Empty;
        EntryQuantity = string.Empty;
        EntryRate = string.Empty;
        EntryError = null;

        if (!keepDefaults)
        {
            EntryHsnCode = string.Empty;
            EntryUnit = DefaultUnit;
            EntryGstRate = DefaultGstRate;
        }
    }

    private static decimal ParseNumber(string text, string field, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            problems.Add($"{field} is required.");
            return 0;
        }

        if (decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var value))
        {
            return value;
        }

        problems.Add($"{field} must be a number.");
        return 0;
    }

    // ---------- Totals ----------
    private void Recalculate()
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            Lines[i].LineNumber = i + 1;
        }

        _lastResult = null;

        if (Lines.Count == 0)
        {
            SetTotals(taxable: 0m, result: null);
            return;
        }

        if (_company is null || SelectedCustomer is null)
        {
            // Tax type is unknown until a customer is chosen; show taxable values only.
            foreach (var line in Lines)
            {
                line.Apply(null, GstCalculator.CalculateLine(line.ToGstInput(), TaxType.IntraState).TaxableAmount);
            }

            SetTotals(Lines.Sum(l => GstCalculator.CalculateLine(l.ToGstInput(), TaxType.IntraState).TaxableAmount), null);
            return;
        }

        _lastResult = GstCalculator.Calculate(
            Lines.Select(l => l.ToGstInput()).ToList(), _company.StateCode, SelectedCustomer.StateCode);

        for (var i = 0; i < Lines.Count; i++)
        {
            Lines[i].Apply(_lastResult.Lines[i], _lastResult.Lines[i].TaxableAmount);
        }

        SetTotals(_lastResult.TaxableTotal, _lastResult);
    }

    private void SetTotals(decimal taxable, GstInvoiceResult? result)
    {
        TaxableTotalText = IndianFormat.Amount(taxable);
        CgstText = result is null ? string.Empty : IndianFormat.Amount(result.CgstTotal);
        SgstText = result is null ? string.Empty : IndianFormat.Amount(result.SgstTotal);
        IgstText = result is null ? string.Empty : IndianFormat.Amount(result.IgstTotal);
        RoundOffText = result is null ? string.Empty : IndianFormat.Amount(result.RoundOff);
        GrandTotalText = result is null ? "—" : IndianFormat.Amount(result.GrandTotal);
        AmountInWordsText = result is not null
            ? AmountInWords.ToRupees(result.GrandTotal)
            : Lines.Count > 0 ? "Select a customer to calculate GST." : string.Empty;

        OnPropertyChanged(nameof(TotalQuantityText));
        OnPropertyChanged(nameof(TaxableTotalText));
        OnPropertyChanged(nameof(CgstText));
        OnPropertyChanged(nameof(SgstText));
        OnPropertyChanged(nameof(IgstText));
        OnPropertyChanged(nameof(RoundOffText));
        OnPropertyChanged(nameof(GrandTotalText));
        OnPropertyChanged(nameof(AmountInWordsText));
        OnPropertyChanged(nameof(ShowCgstSgst));
        OnPropertyChanged(nameof(ShowIgst));
    }

    // ---------- Customer quick-add ----------
    private async Task AddNewCustomerAsync()
    {
        var id = _dialogs.ShowNewCustomerDialog();
        if (id is not null)
        {
            await ReloadCustomersAsync(id);
        }
    }

    // ---------- Save / cancel ----------
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        if (!string.IsNullOrWhiteSpace(EntryDescription))
        {
            ErrorMessage = $"The item \"{EntryDescription.Trim()}\" is typed in the entry row but not added yet. " +
                           $"Click \"{AddLineButtonText}\" first, or clear the description.";
            return;
        }

        var request = new CreateInvoiceRequest
        {
            CustomerId = SelectedCustomer?.Id ?? 0,
            InvoiceDate = InvoiceDate is { } d ? DateOnly.FromDateTime(d) : default,
            BuyerOrderNumber = BuyerOrderNumber,
            BuyerOrderDate = BuyerOrderDate is { } od ? DateOnly.FromDateTime(od) : null,
            PaymentTerms = PaymentTerms,
            DispatchDocumentNumber = DispatchDocumentNumber,
            DispatchedThrough = DispatchedThrough,
            Destination = Destination,
            TermsOfDelivery = TermsOfDelivery,
            Items = Lines.Select(l => l.Input).ToList()
        };

        if (SelectedCustomer is not null && _lastResult is not null)
        {
            var question =
                $"Save this invoice for {SelectedCustomer.Name}?\n\n" +
                $"Items: {Lines.Count}    Grand total: Rs. {IndianFormat.Amount(_lastResult.GrandTotal)}\n\n" +
                "After saving, the invoice cannot be changed. If there is a mistake, " +
                "you can cancel it and create a new one.";

            if (!_dialogs.Confirm(question, "Save invoice"))
            {
                return;
            }
        }

        try
        {
            SavedInvoice = await _invoices.CreateAsync(request);
        }
        catch (ValidationException ex)
        {
            ErrorMessage = "Please correct the following:\n• " + string.Join("\n• ", ex.Errors);
        }
    }

    private async Task CancelSavedInvoiceAsync()
    {
        if (SavedInvoice is null)
        {
            return;
        }

        if (await _actions.CancelAsync(SavedInvoice.Id, SavedInvoice.InvoiceNumber))
        {
            IsSavedInvoiceCancelled = true;
        }
    }
}
