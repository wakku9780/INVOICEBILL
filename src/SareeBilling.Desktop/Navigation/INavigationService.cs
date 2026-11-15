using SareeBilling.Desktop.ViewModels;

namespace SareeBilling.Desktop.Navigation;

public interface INavigationService
{
    AppPage? CurrentPageKey { get; }

    PageViewModel? CurrentPage { get; }

    event EventHandler? Navigated;

    /// <returns>False if the current page asked to stay (unsaved work).</returns>
    bool NavigateTo(AppPage page);
}
