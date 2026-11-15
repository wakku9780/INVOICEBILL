using System.Windows.Input;
using SareeBilling.Application.Common;
using SareeBilling.Application.Customers;
using SareeBilling.Desktop.Mvvm;

namespace SareeBilling.Desktop.ViewModels;

/// <summary>"New customer" pop-up used from the invoice screen.</summary>
public sealed class NewCustomerDialogViewModel : ObservableObject
{
    private readonly ICustomerService _customers;
    private string? _errorMessage;

    public NewCustomerDialogViewModel(ICustomerService customers)
    {
        _customers = customers;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, false));
    }

    /// <summary>Raised with true after a successful save, false on cancel.</summary>
    public event EventHandler<bool>? CloseRequested;

    public CustomerEditorViewModel Editor { get; } = new();

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public int? SavedCustomerId { get; private set; }

    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

    private async Task SaveAsync()
    {
        ErrorMessage = null;
        try
        {
            SavedCustomerId = await _customers.SaveAsync(Editor.ToDto());
            CloseRequested?.Invoke(this, true);
        }
        catch (ValidationException ex)
        {
            ErrorMessage = "Please correct the following:\n• " + string.Join("\n• ", ex.Errors);
        }
    }
}
