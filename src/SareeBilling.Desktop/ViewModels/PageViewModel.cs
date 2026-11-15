using SareeBilling.Desktop.Mvvm;

namespace SareeBilling.Desktop.ViewModels;

/// <summary>Base class for every screen shown in the main content area.</summary>
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    /// <summary>Called once when the page is opened; load data from the database here.</summary>
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Called before leaving the page. Return false to stay (e.g. after the user
    /// chose not to discard unsaved work).
    /// </summary>
    public virtual bool ConfirmLeave() => true;
}
