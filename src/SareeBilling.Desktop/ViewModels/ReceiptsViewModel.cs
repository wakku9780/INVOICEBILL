using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SareeBilling.Application.Common;
using SareeBilling.Application.Receivables;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Navigation;
using SareeBilling.Desktop.Services;
using SareeBilling.Domain.Common;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Desktop.ViewModels;

public sealed record ReceiptRowView(ReceiptSummary Receipt)
{
    public string Number => Receipt.ReceiptNumber;
    public string DateText => Receipt.ReceiptDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    public string CustomerName => Receipt.CustomerName;
    public string ModeText => PaymentModes.Label(Receipt.Mode) + (string.IsNullOrWhiteSpace(Receipt.Reference) ? "" : " · " + Receipt.Reference);
    public string AmountText => IndianFormat.Amount(Receipt.Amount);
    public bool IsCancelled => Receipt.Status == VoucherStatus.Cancelled;
}

public sealed class ReceiptsViewModel : PageViewModel
{
    private readonly IReceiptService _receipts;
    private readonly IAccountsPrintService _printing;
    private readonly IDocumentActions _documents;
    private readonly IDialogService _dialogs;
    private readonly ErrorLog _errorLog;

    private string _searchText = string.Empty;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private bool _includeCancelled = true;
    private ReceiptRowView? _selected;
    private string _summary = string.Empty;
    private int _searchVersion;

    public ReceiptsViewModel(
        IReceiptService receipts,
        IAccountsPrintService printing,
        IDocumentActions documents,
        IDialogService dialogs,
        INavigationService navigation,
        ErrorLog errorLog)
    {
        _receipts = receipts;
        _printing = printing;
        _documents = documents;
        _dialogs = dialogs;
        _errorLog = errorLog;

        OpenCommand = new AsyncRelayCommand(OpenAsync, () => Selected is not null);
        CancelReceiptCommand = new AsyncRelayCommand(CancelAsync, () => Selected is { IsCancelled: false });
        NewReceiptCommand = new RelayCommand(() => navigation.NavigateTo(AppPage.ReceivePayment));
        SearchCommand = new AsyncRelayCommand(RefreshAsync);
    }

    public override string Title => "Receipts";

    public ObservableCollection<ReceiptRowView> Receipts { get; } = [];

    public ICommand OpenCommand { get; }
    public ICommand CancelReceiptCommand { get; }
    public ICommand NewReceiptCommand { get; }
    public ICommand SearchCommand { get; }

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

    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            if (SetProperty(ref _fromDate, value))
            {
                RefreshInBackground();
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
                RefreshInBackground();
            }
        }
    }

    public bool IncludeCancelled
    {
        get => _includeCancelled;
        set
        {
            if (SetProperty(ref _includeCancelled, value))
            {
                RefreshInBackground();
            }
        }
    }

    public ReceiptRowView? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public override Task InitializeAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        var version = ++_searchVersion;
        var results = await _receipts.SearchAsync(new ReceiptSearchCriteria
        {
            Text = SearchText,
            FromDate = FromDate is { } f ? DateOnly.FromDateTime(f) : null,
            ToDate = ToDate is { } t ? DateOnly.FromDateTime(t) : null,
            IncludeCancelled = IncludeCancelled
        });

        if (version != _searchVersion)
        {
            return;
        }

        Receipts.Clear();
        foreach (var r in results)
        {
            Receipts.Add(new ReceiptRowView(r));
        }

        var posted = results.Where(r => r.Status == VoucherStatus.Posted).ToList();
        Summary = $"{(results.Count == 1 ? "1 receipt" : $"{results.Count} receipts")} · " +
                  $"total received ₹ {IndianFormat.Amount(posted.Sum(r => r.Amount))} (cancelled not included)";
    }

    private async void RefreshInBackground()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _errorLog.Write("Receipt search failed", ex);
            _dialogs.ShowError("Could not search receipts.\n\n" + ex.Message);
        }
    }

    private async Task OpenAsync()
    {
        Mouse.OverrideCursor = Cursors.Wait;
        DocumentPrintout document;
        try
        {
            document = await _printing.ReceiptAsync(Selected!.Receipt.Id);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        _documents.Preview(document);
    }

    private async Task CancelAsync()
    {
        var row = Selected!;
        var reason = _dialogs.PromptForText(
            "Cancel receipt",
            $"Why is receipt {row.Number} ({row.CustomerName}, ₹ {row.AmountText}) being cancelled?\n\n" +
            "The bills it paid become unpaid again. The receipt stays in this list marked CANCELLED.",
            "Cancel receipt");
        if (reason is null)
        {
            return;
        }

        try
        {
            await _receipts.CancelAsync(row.Receipt.Id, reason);
            await RefreshAsync();
        }
        catch (ValidationException ex)
        {
            _dialogs.ShowError(string.Join("\n", ex.Errors));
        }
    }
}
