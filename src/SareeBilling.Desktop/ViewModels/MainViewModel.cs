using System.Windows.Input;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Navigation;
using SareeBilling.Desktop.Services;

namespace SareeBilling.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly IUpdateCoordinator _updates;

    public MainViewModel(INavigationService navigation, IUpdateCoordinator updates)
    {
        _navigation = navigation;
        _navigation.Navigated += OnNavigated;

        _updates = updates;
        _updates.AvailableUpdateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsUpdateBannerVisible));
            OnPropertyChanged(nameof(UpdateBannerText));
        };
        ShowUpdateCommand = new RelayCommand(_updates.ShowUpdateDetails);
        RemindUpdateLaterCommand = new RelayCommand(() =>
        {
            if (_updates.AvailableUpdate is { } release)
            {
                _updates.RemindLater(release);
            }
        });

        NavigateCommand = new RelayCommand<NavigationItem>(item => _navigation.NavigateTo(item.Page));
        OpenPageCommand = new RelayCommand<AppPage>(page => _navigation.NavigateTo(page));
    }

    /// <summary>A small banner (not a pop-up) so an update never interrupts invoice work.</summary>
    public bool IsUpdateBannerVisible => _updates.AvailableUpdate is not null;

    public string UpdateBannerText => _updates.AvailableUpdate is { } release
        ? $"A new version of Saree Billing ({release.Version}) is available."
        : string.Empty;

    public ICommand ShowUpdateCommand { get; }

    public ICommand RemindUpdateLaterCommand { get; }

    /// <summary>Sidebar item click: opens that page (the current page may refuse to close).</summary>
    public ICommand NavigateCommand { get; }

    /// <summary>Keyboard shortcuts pass the target AppPage as CommandParameter.</summary>
    public ICommand OpenPageCommand { get; }

    /// <summary>
    /// The sidebar. Only screens that exist are listed; new groups (PURCHASE, ACCOUNTS, REPORTS)
    /// are added when their screens are built.
    /// </summary>
    public IReadOnlyList<NavigationGroup> NavigationGroups { get; } =
    [
        new(null,
        [
            new(AppPage.Dashboard, "Dashboard", "", "Ctrl+D")
        ]),
        new("SALES",
        [
            new(AppPage.CreateInvoice, "Sales Invoice", "", "F8"),
            new(AppPage.InvoiceHistory, "Invoice History", "", "Ctrl+H"),
            new(AppPage.Customers, "Customers", "")
        ]),
        new("SETTINGS",
        [
            new(AppPage.CompanySettings, "Company Settings", ""),
            new(AppPage.BackupRestore, "Backup & Restore", "")
        ])
    ];

    public PageViewModel? CurrentPage => _navigation.CurrentPage;

    public string AppVersion { get; } =
        "Version " + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

    /// <summary>Asked when the main window is closing; false keeps the app open.</summary>
    public bool ConfirmClose() => _navigation.CurrentPage?.ConfirmLeave() ?? true;

    private void OnNavigated(object? sender, EventArgs e)
    {
        // Keep the sidebar highlight in sync wherever navigation started (sidebar, shortcut,
        // dashboard button), and open the group that holds the current page.
        foreach (var group in NavigationGroups)
        {
            foreach (var item in group.Items)
            {
                item.IsSelected = item.Page == _navigation.CurrentPageKey;
                if (item.IsSelected)
                {
                    group.IsExpanded = true;
                }
            }
        }

        OnPropertyChanged(nameof(CurrentPage));
    }
}
