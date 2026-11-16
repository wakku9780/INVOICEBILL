using Microsoft.Extensions.DependencyInjection;
using SareeBilling.Desktop.Services;
using SareeBilling.Desktop.ViewModels;

namespace SareeBilling.Desktop.Navigation;

/// <summary>
/// Creates a fresh view model each time a page is opened, so every screen
/// shows current data from the database.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;
    private readonly IDialogService _dialogs;
    private readonly ErrorLog _errorLog;

    public NavigationService(IServiceProvider services, IDialogService dialogs, ErrorLog errorLog)
    {
        _services = services;
        _dialogs = dialogs;
        _errorLog = errorLog;
    }

    public AppPage? CurrentPageKey { get; private set; }

    public PageViewModel? CurrentPage { get; private set; }

    public event EventHandler? Navigated;

    public bool NavigateTo(AppPage page)
    {
        if (CurrentPageKey == page)
        {
            return true;
        }

        if (CurrentPage is not null && !CurrentPage.ConfirmLeave())
        {
            return false;
        }

        var viewModel = CreatePage(page);
        CurrentPage = viewModel;
        CurrentPageKey = page;
        Navigated?.Invoke(this, EventArgs.Empty);

        InitializePage(viewModel);
        return true;
    }

    private async void InitializePage(PageViewModel page)
    {
        try
        {
            await page.InitializeAsync();
        }
        catch (Exception ex)
        {
            _errorLog.Write($"Loading page '{page.Title}' failed", ex);
            _dialogs.ShowError($"Could not load {page.Title}.\n\n{ex.Message}");
        }
    }

    private PageViewModel CreatePage(AppPage page) => page switch
    {
        AppPage.Dashboard => _services.GetRequiredService<DashboardViewModel>(),
        AppPage.CreateInvoice => _services.GetRequiredService<CreateInvoiceViewModel>(),
        AppPage.Customers => _services.GetRequiredService<CustomersViewModel>(),
        AppPage.InvoiceHistory => _services.GetRequiredService<InvoiceHistoryViewModel>(),
        AppPage.CompanySettings => _services.GetRequiredService<CompanySettingsViewModel>(),
        AppPage.BackupRestore => _services.GetRequiredService<BackupRestoreViewModel>(),
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, null)
    };
}
