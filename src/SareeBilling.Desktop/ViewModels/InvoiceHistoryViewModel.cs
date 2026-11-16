using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SareeBilling.Application.Invoices;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Services;
using SareeBilling.Domain.Common;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Desktop.ViewModels;

/// <summary>One row in the history table, pre-formatted for display.</summary>
public sealed class InvoiceRowViewModel
{
    public InvoiceRowViewModel(InvoiceSummary summary)
    {
        Summary = summary;
    }

    public InvoiceSummary Summary { get; }

    public int Id => Summary.Id;
    public string InvoiceNumber => Summary.InvoiceNumber;
    public string DateText => Summary.InvoiceDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
    public string BuyerName => Summary.BuyerName;
    public string TaxableText => IndianFormat.Amount(Summary.TaxableTotal);
    public string TaxText => IndianFormat.Amount(Summary.TotalTax);
    public string GrandTotalText => IndianFormat.Amount(Summary.GrandTotal);
    public bool IsCancelled => Summary.Status == InvoiceStatus.Cancelled;
    public string StatusText => IsCancelled ? "Cancelled" : "Final";
}

public sealed class InvoiceHistoryViewModel : PageViewModel
{
    private readonly IInvoiceService _invoices;
    private readonly IInvoiceActions _actions;
    private readonly IDialogService _dialogs;
    private readonly ErrorLog _errorLog;
    private readonly DateTime _today;

    private string _searchText = string.Empty;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private bool _includeCancelled = true;
    private InvoiceRowViewModel? _selectedInvoice;
    private string _summaryText = string.Empty;
    private int _searchVersion;
    private bool _suppressRefresh;

    public InvoiceHistoryViewModel(
        IInvoiceService invoices,
        IInvoiceActions actions,
        IDialogService dialogs,
        ErrorLog errorLog,
        TimeProvider timeProvider)
    {
        _invoices = invoices;
        _actions = actions;
        _dialogs = dialogs;
        _errorLog = errorLog;
        _today = timeProvider.GetLocalNow().Date;

        PreviewCommand = new AsyncRelayCommand(() => _actions.PreviewAsync(SelectedInvoice!.Id), () => SelectedInvoice is not null);
        PrintCommand = new AsyncRelayCommand(() => _actions.PrintAsync(SelectedInvoice!.Id), () => SelectedInvoice is not null);
        SavePdfCommand = new AsyncRelayCommand(() => _actions.SavePdfAsync(SelectedInvoice!.Id), () => SelectedInvoice is not null);
        CancelInvoiceCommand = new AsyncRelayCommand(CancelSelectedAsync, () => SelectedInvoice is { IsCancelled: false });
        ThisMonthCommand = new RelayCommand(() => SetRange(new DateTime(_today.Year, _today.Month, 1), _today));
        ThisYearCommand = new RelayCommand(() => SetRange(FinancialYearStart(_today), _today));
        AllDatesCommand = new RelayCommand(() => SetRange(null, null));

        _fromDate = FinancialYearStart(_today);
        _toDate = _today;
    }

    public override string Title => "Invoice History";

    public ObservableCollection<InvoiceRowViewModel> Invoices { get; } = [];

    public ICommand PreviewCommand { get; }
    public ICommand PrintCommand { get; }
    public ICommand SavePdfCommand { get; }
    public ICommand CancelInvoiceCommand { get; }
    public ICommand ThisMonthCommand { get; }
    public ICommand ThisYearCommand { get; }
    public ICommand AllDatesCommand { get; }

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

    public InvoiceRowViewModel? SelectedInvoice
    {
        get => _selectedInvoice;
        set
        {
            if (SetProperty(ref _selectedInvoice, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }

    public override Task InitializeAsync() => RefreshAsync();

    private async Task RefreshAsync(int? selectId = null)
    {
        var version = ++_searchVersion;
        var results = await _invoices.SearchAsync(new InvoiceSearchCriteria
        {
            Text = SearchText,
            FromDate = FromDate is { } from ? DateOnly.FromDateTime(from) : null,
            ToDate = ToDate is { } to ? DateOnly.FromDateTime(to) : null,
            IncludeCancelled = IncludeCancelled
        });

        // Typing quickly starts several searches; only the newest one updates the list.
        if (version != _searchVersion)
        {
            return;
        }

        Invoices.Clear();
        foreach (var summary in results)
        {
            Invoices.Add(new InvoiceRowViewModel(summary));
        }

        SelectedInvoice = selectId is null ? null : Invoices.FirstOrDefault(i => i.Id == selectId);
        SummaryText = BuildSummary(results);
    }

    private static string BuildSummary(IReadOnlyList<InvoiceSummary> results)
    {
        if (results.Count == 0)
        {
            return "No invoices found.";
        }

        var active = results.Where(r => r.Status != InvoiceStatus.Cancelled).ToList();
        var cancelled = results.Count - active.Count;

        var text = $"{active.Count} invoice{(active.Count == 1 ? string.Empty : "s")}   •   " +
                   $"Taxable Rs. {IndianFormat.Amount(active.Sum(r => r.TaxableTotal))}   •   " +
                   $"GST Rs. {IndianFormat.Amount(active.Sum(r => r.TotalTax))}   •   " +
                   $"Total Rs. {IndianFormat.Amount(active.Sum(r => r.GrandTotal))}";

        if (cancelled > 0)
        {
            text += $"   ({cancelled} cancelled, not included in totals)";
        }

        if (results.Count >= InvoiceService.MaxSearchResults)
        {
            text += $"   Showing the latest {InvoiceService.MaxSearchResults} only — narrow the dates to see older ones.";
        }

        return text;
    }

    private async void RefreshInBackground()
    {
        if (_suppressRefresh)
        {
            return;
        }

        try
        {
            await RefreshAsync(SelectedInvoice?.Id);
        }
        catch (Exception ex)
        {
            _errorLog.Write("Invoice search failed", ex);
            _dialogs.ShowError("Could not search invoices.\n\n" + ex.Message);
        }
    }

    private void SetRange(DateTime? from, DateTime? to)
    {
        _suppressRefresh = true;
        FromDate = from;
        ToDate = to;
        _suppressRefresh = false;
        RefreshInBackground();
    }

    private async Task CancelSelectedAsync()
    {
        if (SelectedInvoice is not { } row)
        {
            return;
        }

        if (await _actions.CancelAsync(row.Id, row.InvoiceNumber))
        {
            await RefreshAsync(row.Id);
        }
    }

    private static DateTime FinancialYearStart(DateTime date) =>
        new(date.Month >= 4 ? date.Year : date.Year - 1, 4, 1);
}
