using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Navigation;

namespace SareeBilling.Desktop.ViewModels;

/// <summary>One entry in the sidebar. Glyph is a Segoe MDL2 Assets icon code.</summary>
public sealed record NavigationItem(AppPage Page, string Title, string Glyph);

public sealed class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private NavigationItem? _selectedNavigationItem;

    public MainViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        _navigation.Navigated += OnNavigated;
    }

    public IReadOnlyList<NavigationItem> NavigationItems { get; } =
    [
        new(AppPage.Dashboard, "Dashboard", ""),
        new(AppPage.CreateInvoice, "Create Invoice", ""),
        new(AppPage.Customers, "Customers", ""),
        new(AppPage.InvoiceHistory, "Invoice History", ""),
        new(AppPage.CompanySettings, "Company Settings", ""),
        new(AppPage.BackupRestore, "Backup & Restore", "")
    ];

    public PageViewModel? CurrentPage => _navigation.CurrentPage;

    public string AppVersion { get; } =
        "Version " + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

    /// <summary>Bound to the sidebar list; selecting an item opens that page.</summary>
    public NavigationItem? SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            var previous = _selectedNavigationItem;
            if (SetProperty(ref _selectedNavigationItem, value) && value is not null &&
                !_navigation.NavigateTo(value.Page))
            {
                // The page refused to close: put the sidebar highlight back once the
                // list box has finished its own selection change.
                _selectedNavigationItem = previous;
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    () => OnPropertyChanged(nameof(SelectedNavigationItem)));
            }
        }
    }

    /// <summary>Asked when the main window is closing; false keeps the app open.</summary>
    public bool ConfirmClose() => _navigation.CurrentPage?.ConfirmLeave() ?? true;

    private void OnNavigated(object? sender, EventArgs e)
    {
        // Keep the sidebar highlight in sync when navigation starts elsewhere
        // (e.g. a dashboard quick-action button).
        var match = NavigationItems.FirstOrDefault(i => i.Page == _navigation.CurrentPageKey);
        if (!Equals(match, _selectedNavigationItem))
        {
            _selectedNavigationItem = match;
            OnPropertyChanged(nameof(SelectedNavigationItem));
        }

        OnPropertyChanged(nameof(CurrentPage));
    }
}
