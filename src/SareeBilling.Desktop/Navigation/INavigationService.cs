using SareeBilling.Desktop.ViewModels;

namespace SareeBilling.Desktop.Navigation;

public interface INavigationService
{
    AppPage? CurrentPageKey { get; }

    PageViewModel? CurrentPage { get; }

    event EventHandler? Navigated;

    /// <returns>False if the current page asked to stay (unsaved work).</returns>
    bool NavigateTo(AppPage page);

    /// <summary>
    /// Opens a page with a value for it, e.g. the customer whose ledger to show. The page is always
    /// opened afresh, even if it is already the current page.
    /// </summary>
    bool NavigateTo(AppPage page, object parameter);
}
