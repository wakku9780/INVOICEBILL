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

public sealed record CreditNoteRowView(CreditNoteSummary Note)
{
    public string Number => Note.CreditNoteNumber;
    public string DateText => Note.CreditNoteDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    public string InvoiceNumber => Note.InvoiceNumber;
    public string CustomerName => Note.BuyerName;
    public string KindText => CreditNoteKinds.Label(Note.Kind);
    public string Reason => Note.Reason;
    public string GstText => IndianFormat.Amount(Note.TotalTax);
    public string AmountText => IndianFormat.Amount(Note.GrandTotal);
    public bool IsCancelled => Note.Status == VoucherStatus.Cancelled;
}

public sealed class CreditNotesViewModel : PageViewModel
{
    private readonly ICreditNoteService _creditNotes;
    private readonly IAccountsPrintService _printing;
    private readonly IDocumentActions _documents;
    private readonly IDialogService _dialogs;
    private readonly ErrorLog _errorLog;

    private string _searchText = string.Empty;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private bool _includeCancelled = true;
    private CreditNoteRowView? _selected;
    private string _summary = string.Empty;
    private int _searchVersion;

    public CreditNotesViewModel(
        ICreditNoteService creditNotes,
        IAccountsPrintService printing,
        IDocumentActions documents,
        IDialogService dialogs,
        INavigationService navigation,
        ErrorLog errorLog)
    {
        _creditNotes = creditNotes;
        _printing = printing;
        _documents = documents;
        _dialogs = dialogs;
        _errorLog = errorLog;

        OpenCommand = new AsyncRelayCommand(OpenAsync, () => Selected is not null);
        CancelCreditNoteCommand = new AsyncRelayCommand(CancelAsync, () => Selected is { IsCancelled: false });
        NewCreditNoteCommand = new RelayCommand(() => navigation.NavigateTo(AppPage.CreditNote));
        SearchCommand = new AsyncRelayCommand(RefreshAsync);
    }

    public override string Title => "Credit Notes";

    public ObservableCollection<CreditNoteRowView> CreditNotes { get; } = [];

    public ICommand OpenCommand { get; }
    public ICommand CancelCreditNoteCommand { get; }
    public ICommand NewCreditNoteCommand { get; }
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

    public CreditNoteRowView? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public override Task InitializeAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        var version = ++_searchVersion;
        var results = await _creditNotes.SearchAsync(new CreditNoteSearchCriteria
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

        CreditNotes.Clear();
        foreach (var n in results)
        {
            CreditNotes.Add(new CreditNoteRowView(n));
        }

        var posted = results.Where(n => n.Status == VoucherStatus.Posted).ToList();
        Summary = $"{(results.Count == 1 ? "1 credit note" : $"{results.Count} credit notes")} · " +
                  $"total ₹ {IndianFormat.Amount(posted.Sum(n => n.GrandTotal))}, " +
                  $"GST ₹ {IndianFormat.Amount(posted.Sum(n => n.TotalTax))} (cancelled not included)";
    }

    private async void RefreshInBackground()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _errorLog.Write("Credit note search failed", ex);
            _dialogs.ShowError("Could not search credit notes.\n\n" + ex.Message);
        }
    }

    private async Task OpenAsync()
    {
        Mouse.OverrideCursor = Cursors.Wait;
        DocumentPrintout document;
        try
        {
            document = await _printing.CreditNoteAsync(Selected!.Note.Id);
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
            "Cancel credit note",
            $"Why is credit note {row.Number} ({row.CustomerName}, ₹ {row.AmountText}) being cancelled?\n\n" +
            $"Invoice {row.InvoiceNumber} goes back to its full value, and any bill this credit was used for becomes " +
            "unpaid again. The credit note stays in this list marked CANCELLED.",
            "Cancel credit note");
        if (reason is null)
        {
            return;
        }

        try
        {
            await _creditNotes.CancelAsync(row.Note.Id, reason);
            await RefreshAsync();
        }
        catch (ValidationException ex)
        {
            _dialogs.ShowError(string.Join("\n", ex.Errors));
        }
    }
}
