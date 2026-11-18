using SareeBilling.Desktop.Mvvm;

namespace SareeBilling.Desktop.ViewModels;

/// <summary>Base class for every screen shown in the main content area.</summary>
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    /// <summary>Called once when the page is opened; load data from the database here.</summary>
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <summary>A value passed by the page that opened this one (e.g. a customer Id). Called before <see cref="InitializeAsync"/>.</summary>
    public virtual void ReceiveParameter(object parameter)
    {
    }

    /// <summary>
    /// Called before leaving the page. Return false to stay (e.g. after the user
    /// chose not to discard unsaved work).
    /// </summary>
    public virtual bool ConfirmLeave() => true;

    /// <summary>
    /// True while the user is in the middle of work that would be lost if the app closed
    /// (e.g. an invoice being entered). App updates wait until this is false.
    /// </summary>
    public virtual bool HasUnsavedWork => false;
}
