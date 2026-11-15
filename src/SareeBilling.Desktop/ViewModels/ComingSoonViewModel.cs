namespace SareeBilling.Desktop.ViewModels;

/// <summary>Temporary page for screens that are not built yet.</summary>
public sealed class ComingSoonViewModel : PageViewModel
{
    public ComingSoonViewModel(string title)
    {
        Title = title;
    }

    public override string Title { get; }
}
