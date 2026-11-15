using System.Collections.ObjectModel;
using System.Windows.Input;
using SareeBilling.Application.Common;
using SareeBilling.Application.Customers;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Services;

namespace SareeBilling.Desktop.ViewModels;

public sealed class CustomersViewModel : PageViewModel
{
    private readonly ICustomerService _service;
    private readonly IDialogService _dialogs;
    private readonly ErrorLog _errorLog;

    private string _searchText = string.Empty;
    private bool _showInactive;
    private CustomerDto? _selectedCustomer;
    private string? _errorMessage;
    private string? _successMessage;
    private int _searchVersion;

    public CustomersViewModel(ICustomerService service, IDialogService dialogs, ErrorLog errorLog)
    {
        _service = service;
        _dialogs = dialogs;
        _errorLog = errorLog;

        NewCommand = new RelayCommand(StartNew);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => !Editor.IsNew);
    }

    public override string Title => "Customers";

    public ObservableCollection<CustomerDto> Customers { get; } = [];

    public CustomerEditorViewModel Editor { get; } = new();

    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }

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

    public CustomerDto? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value) && value is not null)
            {
                Editor.Load(value);
                ClearMessages();
            }
        }
    }

    public string ResultSummary => Customers.Count == 1 ? "1 customer" : $"{Customers.Count} customers";

    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public string? SuccessMessage { get => _successMessage; private set => SetProperty(ref _successMessage, value); }

    public override Task InitializeAsync() => RefreshAsync();

    private async Task RefreshAsync(int? selectId = null)
    {
        var version = ++_searchVersion;
        var results = await _service.SearchAsync(SearchText, ShowInactive);

        // Typing quickly starts several searches; only the newest one updates the list.
        if (version != _searchVersion)
        {
            return;
        }

        Customers.Clear();
        foreach (var customer in results)
        {
            Customers.Add(customer);
        }

        OnPropertyChanged(nameof(ResultSummary));

        if (selectId is not null)
        {
            _selectedCustomer = Customers.FirstOrDefault(c => c.Id == selectId);
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

    private void StartNew()
    {
        SelectedCustomer = null;
        Editor.Clear();
        ClearMessages();
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
            SuccessMessage = wasNew ? "Customer added." : "Changes saved.";
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
            ? $"{name} already has invoices, so it cannot be deleted.\n\n" +
              "Do you want to hide this customer instead? Old invoices are kept, " +
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
        await RefreshAsync();

        SuccessMessage = result == CustomerRemovalResult.Deleted
            ? $"{name} deleted."
            : $"{name} hidden. Tick \"Show inactive\" to see it.";
    }

    private void ClearMessages()
    {
        ErrorMessage = null;
        SuccessMessage = null;
    }
}
