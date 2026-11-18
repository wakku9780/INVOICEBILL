using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Navigation;

namespace SareeBilling.Desktop.ViewModels;

/// <summary>One screen in the sidebar. Glyph is a Segoe MDL2 Assets icon code.</summary>
public sealed class NavigationItem : ObservableObject
{
    private bool _isSelected;

    public NavigationItem(AppPage page, string title, string glyph, string? shortcut = null)
    {
        Page = page;
        Title = title;
        Glyph = glyph;
        Shortcut = shortcut;
    }

    public AppPage Page { get; }

    public string Title { get; }

    public string Glyph { get; }

    /// <summary>Keyboard shortcut shown next to the title, e.g. "F8".</summary>
    public string? Shortcut { get; }

    public string AutomationName => Shortcut is null ? Title : $"{Title} ({Shortcut})";

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary>
/// A collapsible group of sidebar items (SALES, SETTINGS, ...). A group without a title shows its
/// items at the top level with no header.
/// </summary>
public sealed class NavigationGroup : ObservableObject
{
    private bool _isExpanded = true;

    public NavigationGroup(string? title, IReadOnlyList<NavigationItem> items)
    {
        Title = title;
        Items = items;
    }

    public string? Title { get; }

    public bool HasHeader => Title is not null;

    public IReadOnlyList<NavigationItem> Items { get; }

    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
}
