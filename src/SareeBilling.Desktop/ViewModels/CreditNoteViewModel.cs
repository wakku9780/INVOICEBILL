using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SareeBilling.Application.Common;
using SareeBilling.Application.CreditNotes;
using SareeBilling.Application.Receivables;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Navigation;
using SareeBilling.Desktop.Services;
using SareeBilling.Domain.Common;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Desktop.ViewModels;

/// <summary>One line of the original invoice; the user types the quantity returned or the amount to reduce.</summary>
public sealed class CreditLineRow : ObservableObject
{
    private string _inputText = string.Empty;
    private string _creditText = string.Empty;

    public CreditLineRow(CreditableLine line)
    {
        Line = line;
    }

    public CreditableLine Line { get; }

    public int LineNumber => Line.LineNumber;
    public string Description => Line.Description;
    public string HsnCode => Line.HsnCode;
    public string BilledText => $"{IndianFormat.Quantity(Line.Quantity)} {Line.Unit}";
    public string RateText => IndianFormat.Amount(Line.Rate);
    public string GstText => IndianFormat.Percent(Line.GstRate) + "%";
    public string ReturnedText => Line.ReturnedQuantity == 0 ? "-" : $"{IndianFormat.Quantity(Line.ReturnedQuantity)} {Line.Unit}";
    public string RemainingQuantityText => $"{IndianFormat.Quantity(Line.RemainingQuantity)} {Line.Unit}";
    public string RemainingValueText => IndianFormat.Amount(Line.RemainingTaxable);
    /// <summary>False once the whole value of the line has been credited.</summary>
    public bool CanCredit => Line.RemainingTaxable > 0;

    public event EventHandler? InputChanged;

    /// <summary>Quantity returned (goods) or amount to reduce before GST (amount only).</summary>
    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetProperty(ref _inputText, value))
            {
                InputChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>0 when empty; null when not a number.</summary>
    public decimal? Input => ReceivePaymentViewModel.ParseAmount(InputText);

    /// <summary>Taxable value credited on this line, worked out from the input.</summary>
    public string CreditText { get => _creditText; set => SetProperty(ref _creditText, value); }
}

public sealed class CreditNoteViewModel : PageViewModel
{
    private readonly ICreditNoteService _creditNotes;
    private readonly IReceiptService _receipts;
    private readonly IAccountsPrintService _printing;
    private readonly IDocumentActions _documents;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly TimeProvider _timeProvider;

    private string _invoiceNumberText = string.Empty;
    private CreditableInvoice? _invoice;
    private decimal _invoicePending;
    private CreditNoteKind _kind = CreditNoteKind.GoodsReturn;
    private DateTime? _creditNoteDate;
    private string _reason = string.Empty;
    private CreditNoteCalculation? _calculation;
    private string? _previewProblem;
    private string? _errorMessage;
    private string? _successMessage;
    private int? _lastSavedId;
    private int? _pendingInvoiceId;

    public CreditNoteViewModel(
        ICreditNoteService creditNotes,
        IReceiptService receipts,
        IAccountsPrintService printing,
        IDocumentActions documents,
        IDialogService dialogs,
        INavigationService navigation,
        TimeProvider timeProvider)
    {
        _creditNotes = creditNotes;
        _receipts = receipts;
        _printing = printing;
        _documents = documents;
        _dialogs = dialogs;
        _navigation = navigation;
        _timeProvider = timeProvider;
        _creditNoteDate = Today.ToDateTime(TimeOnly.MinValue);

        LoadInvoiceCommand = new AsyncRelayCommand(LoadByNumberAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanEdit);
        ReturnAllCommand = new RelayCommand(ReturnAll, () => CanEdit && IsGoodsReturn);
        ClearCommand = new RelayCommand(ClearInputs, () => CanEdit);
        PrintLastCommand = new AsyncRelayCommand(PrintLastAsync, () => _lastSavedId is not null);
        OpenListCommand = new RelayCommand(() => _navigation.NavigateTo(AppPage.CreditNotes));
        OpenLedgerCommand = new RelayCommand(() => _navigation.NavigateTo(AppPage.CustomerLedger, _invoice!.CustomerId), () => _invoice is not null);
    }

    public override string Title => "Credit Note (Sales Return)";

    public ObservableCollection<CreditLineRow> Lines { get; } = [];

    public ICommand LoadInvoiceCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ReturnAllCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand PrintLastCommand { get; }
    public ICommand OpenListCommand { get; }
    public ICommand OpenLedgerCommand { get; }

    public string InvoiceNumberText { get => _invoiceNumberText; set => SetProperty(ref _invoiceNumberText, value); }

    public bool HasInvoice => _invoice is not null;

    /// <summary>A loaded invoice that is not cancelled.</summary>
    public bool CanEdit => _invoice is { IsCancelled: false };

    public string InvoiceHeader => _invoice is null
        ? string.Empty
        : $"Invoice {_invoice.InvoiceNumber} · {LedgerFormat.Date(_invoice.InvoiceDate)} · {_invoice.BuyerName}";

    public string InvoiceDetails => _invoice is null
        ? string.Empty
        : $"Invoice total ₹ {IndianFormat.Amount(_invoice.GrandTotal)} · Pending now ₹ {IndianFormat.Amount(_invoicePending)} · " +
          (_invoice.TaxType == TaxType.IntraState ? "CGST + SGST" : "IGST") +
          (_invoice.CreditNotes.Count == 0
              ? string.Empty
              : " · Already credited: " + string.Join(", ", _invoice.CreditNotes.Select(c => $"{c.CreditNoteNumber} (₹ {IndianFormat.Amount(c.GrandTotal)})")));

    public bool IsInvoiceCancelled => _invoice is { IsCancelled: true };

    public bool IsGoodsReturn
    {
        get => _kind == CreditNoteKind.GoodsReturn;
        set
        {
            if (value)
            {
                SetKind(CreditNoteKind.GoodsReturn);
            }
        }
    }

    public bool IsAmountOnly
    {
        get => _kind == CreditNoteKind.AmountOnly;
        set
        {
            if (value)
            {
                SetKind(CreditNoteKind.AmountOnly);
            }
        }
    }

    public string InputHeader => IsGoodsReturn ? "Qty returned" : "Reduce by (₹)";

    public string RemainingHeader => IsGoodsReturn ? "Can return" : "Value left (₹)";

    public string KindHint => IsGoodsReturn
        ? "Enter how many pieces came back on each line. Value and GST are worked out at the invoice rate."
        : "Enter the reduction before GST on each line (rate difference or discount). GST is added at the line's rate.";

    public DateTime? CreditNoteDate
    {
        get => _creditNoteDate;
        set
        {
            if (SetProperty(ref _creditNoteDate, value))
            {
                OnPropertyChanged(nameof(DeadlineWarning));
            }
        }
    }

    public string Reason { get => _reason; set => SetProperty(ref _reason, value); }

    /// <summary>Shown when the GST time limit for this invoice has passed (warn, but allow).</summary>
    public string? DeadlineWarning
    {
        get
        {
            if (_invoice is null || CreditNoteDate is not { } date)
            {
                return null;
            }

            var deadline = CreditNoteRules.GstDeadline(_invoice.InvoiceDate);
            return CreditNoteRules.IsPastGstDeadline(_invoice.InvoiceDate, DateOnly.FromDateTime(date))
                ? $"GST time limit has passed: GST on invoices dated {LedgerFormat.Date(_invoice.InvoiceDate)} could be reduced only " +
                  $"until {LedgerFormat.Date(deadline)}. You can still save this credit note, but please check with your CA " +
                  "whether the GST can be claimed back."
                : null;
        }
    }

    public string TaxableText => Amount(_calculation?.TaxableTotal);
    public string CgstText => Amount(_calculation?.CgstTotal);
    public string SgstText => Amount(_calculation?.SgstTotal);
    public string IgstText => Amount(_calculation?.IgstTotal);
    public string RoundOffText => Amount(_calculation?.RoundOff);
    public string TotalText => Amount(_calculation?.GrandTotal);
    public bool IsIntraState => _invoice?.TaxType == TaxType.IntraState;
    public bool IsInterState => _invoice?.TaxType == TaxType.InterState;

    /// <summary>What saving will do with the money, e.g. "Reduces pending by ₹ 3,150.00".</summary>
    public string EffectText
    {
        get
        {
            if (_calculation is null || _invoice is null)
            {
                return string.Empty;
            }

            var applied = Math.Min(_invoicePending, _calculation.GrandTotal);
            var advance = _calculation.GrandTotal - applied;
            return advance == 0
                ? $"Reduces what is pending on invoice {_invoice.InvoiceNumber} by ₹ {IndianFormat.Amount(applied)}."
                : applied == 0
                    ? $"Invoice {_invoice.InvoiceNumber} is already paid, so ₹ {IndianFormat.Amount(advance)} becomes {_invoice.BuyerName}'s advance."
                    : $"Reduces invoice {_invoice.InvoiceNumber} by ₹ {IndianFormat.Amount(applied)}; ₹ {IndianFormat.Amount(advance)} becomes advance.";
        }
    }

    public string? PreviewProblem { get => _previewProblem; private set => SetProperty(ref _previewProblem, value); }
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public string? SuccessMessage { get => _successMessage; private set => SetProperty(ref _successMessage, value); }
    public bool CanPrintLast => _lastSavedId is not null;

    public override bool HasUnsavedWork => CanEdit && Lines.Any(l => l.Input is not 0);

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

    public override void ReceiveParameter(object parameter)
    {
        if (parameter is int invoiceId)
        {
            _pendingInvoiceId = invoiceId;
        }
    }

    public override async Task InitializeAsync()
    {
        if (_pendingInvoiceId is { } id)
        {
            await ShowInvoiceAsync(await _creditNotes.GetInvoiceAsync(id));
        }
    }

    public override bool ConfirmLeave() =>
        !HasUnsavedWork || _dialogs.Confirm("The credit note you were entering has not been saved. Leave without saving?", "Leave Credit Note");

    private static string Amount(decimal? value) => value is { } v ? IndianFormat.Amount(v) : "-";

    private async Task LoadByNumberAsync()
    {
        ErrorMessage = null;
        SuccessMessage = null;
        if (string.IsNullOrWhiteSpace(InvoiceNumberText))
        {
            ErrorMessage = "Enter the invoice number the goods or discount relate to.";
            return;
        }

        if (HasUnsavedWork && !_dialogs.Confirm("Discard the credit note you were entering and load another invoice?", "Credit Note"))
        {
            return;
        }

        var invoice = await _creditNotes.FindInvoiceAsync(InvoiceNumberText);
        if (invoice is null)
        {
            ErrorMessage = $"No invoice numbered \"{InvoiceNumberText.Trim()}\" was found. Check the number in Invoice History.";
            return;
        }

        await ShowInvoiceAsync(invoice);
    }

    private async Task ShowInvoiceAsync(CreditableInvoice? invoice, bool keepMessages = false)
    {
        if (!keepMessages)
        {
            ErrorMessage = null;
            SuccessMessage = null;
        }

        _invoice = invoice;
        _invoicePending = 0;
        if (invoice is not null)
        {
            InvoiceNumberText = invoice.InvoiceNumber;
            var account = await _receipts.GetAccountAsync(invoice.CustomerId);
            _invoicePending = account?.Bills.FirstOrDefault(b => b.Ref == new BillRef(BillKind.Invoice, invoice.InvoiceId))?.Pending ?? 0;
            if (invoice.IsCancelled)
            {
                ErrorMessage = $"Invoice {invoice.InvoiceNumber} is cancelled, so no credit note can be made against it.";
            }
        }

        foreach (var row in Lines)
        {
            row.InputChanged -= OnInputChanged;
        }

        Lines.Clear();
        foreach (var line in invoice?.Lines ?? [])
        {
            var row = new CreditLineRow(line);
            row.InputChanged += OnInputChanged;
            Lines.Add(row);
        }

        foreach (var name in new[]
                 {
                     nameof(HasInvoice), nameof(CanEdit), nameof(InvoiceHeader), nameof(InvoiceDetails), nameof(IsInvoiceCancelled),
                     nameof(DeadlineWarning), nameof(IsIntraState), nameof(IsInterState)
                 })
        {
            OnPropertyChanged(name);
        }

        Recalculate();
        CommandManager.InvalidateRequerySuggested();
    }

    private void SetKind(CreditNoteKind kind)
    {
        if (_kind == kind)
        {
            return;
        }

        _kind = kind;
        foreach (var name in new[] { nameof(IsGoodsReturn), nameof(IsAmountOnly), nameof(InputHeader), nameof(RemainingHeader), nameof(KindHint) })
        {
            OnPropertyChanged(name);
        }

        ClearInputs();
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnInputChanged(object? sender, EventArgs e) => Recalculate();

    private void ReturnAll()
    {
        foreach (var row in Lines)
        {
            row.InputText = row.Line.RemainingQuantity > 0 ? IndianFormat.Quantity(row.Line.RemainingQuantity) : string.Empty;
        }
    }

    private void ClearInputs()
    {
        foreach (var row in Lines)
        {
            row.InputText = string.Empty;
        }

        Recalculate();
    }

    private List<CreditNoteLineRequest> Requests() => Lines
        .Where(r => r.Input is not 0)
        .Select(r => IsGoodsReturn
            ? new CreditNoteLineRequest(r.Line.InvoiceItemId, r.Input ?? -1, 0m)
            : new CreditNoteLineRequest(r.Line.InvoiceItemId, 0m, r.Input ?? -1))
        .ToList();

    /// <summary>Works out the totals as the user types, with the same rules used when saving.</summary>
    private void Recalculate()
    {
        _calculation = null;
        PreviewProblem = null;
        foreach (var row in Lines)
        {
            row.CreditText = string.Empty;
        }

        if (_invoice is not null && Lines.Any(r => r.Input is not 0))
        {
            var bad = Lines.FirstOrDefault(r => r.Input is null);
            if (bad is not null)
            {
                PreviewProblem = $"Line {bad.LineNumber}: \"{bad.InputText}\" is not a number.";
            }
            else
            {
                var (calculation, errors) = CreditNoteRules.Calculate(_invoice, _kind, Requests());
                _calculation = calculation;
                PreviewProblem = errors.FirstOrDefault();
                foreach (var line in calculation?.Lines ?? [])
                {
                    Lines.First(r => r.Line.InvoiceItemId == line.Source.InvoiceItemId).CreditText = IndianFormat.Amount(line.TaxableAmount);
                }
            }
        }

        foreach (var name in new[] { nameof(TaxableText), nameof(CgstText), nameof(SgstText), nameof(IgstText), nameof(RoundOffText), nameof(TotalText), nameof(EffectText) })
        {
            OnPropertyChanged(name);
        }
    }

    private async Task SaveAsync()
    {
        ErrorMessage = null;
        SuccessMessage = null;
        Recalculate();
        if (_calculation is null)
        {
            ErrorMessage = PreviewProblem ?? (IsGoodsReturn
                ? "Enter the quantity returned for at least one item."
                : "Enter the amount to reduce for at least one item.");
            return;
        }

        var question =
            $"Save this credit note?\n\n" +
            $"Against: invoice {_invoice!.InvoiceNumber} ({_invoice.BuyerName})\n" +
            $"Type: {CreditNoteKinds.Label(_kind)}\n" +
            $"Value ₹ {IndianFormat.Amount(_calculation.TaxableTotal)} + GST ₹ {IndianFormat.Amount(_calculation.TotalTax)} = " +
            $"₹ {IndianFormat.Amount(_calculation.GrandTotal)}\n" +
            $"{EffectText}\n" +
            (DeadlineWarning is null ? string.Empty : "\nNote: the GST time limit for this invoice has passed.\n") +
            "\nA saved credit note cannot be edited; it can only be cancelled with a reason.";
        if (!_dialogs.Confirm(question, "Save credit note"))
        {
            return;
        }

        try
        {
            var created = await _creditNotes.CreateAsync(new CreateCreditNoteRequest
            {
                InvoiceId = _invoice.InvoiceId,
                CreditNoteDate = CreditNoteDate is { } d ? DateOnly.FromDateTime(d) : default,
                Kind = _kind,
                Reason = Reason,
                Lines = Requests()
            });

            _lastSavedId = created.Id;
            OnPropertyChanged(nameof(CanPrintLast));
            Reason = string.Empty;
            await ShowInvoiceAsync(await _creditNotes.GetInvoiceAsync(_invoice.InvoiceId), keepMessages: true);
            SuccessMessage = $"Credit note {created.CreditNoteNumber} saved: ₹ {IndianFormat.Amount(created.GrandTotal)}" +
                             (created.KeptAsAdvance > 0 ? $" (₹ {IndianFormat.Amount(created.KeptAsAdvance)} kept as advance)." : ".");
        }
        catch (ValidationException ex)
        {
            ErrorMessage = "Please correct the following:\n• " + string.Join("\n• ", ex.Errors);
        }
    }

    private async Task PrintLastAsync()
    {
        Mouse.OverrideCursor = Cursors.Wait;
        DocumentPrintout document;
        try
        {
            document = await _printing.CreditNoteAsync(_lastSavedId!.Value);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        _documents.Preview(document);
    }
}
