using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SareeBilling.Desktop.Converters;

/// <summary>
/// Visible when the bound value is non-null and not an empty string; otherwise Collapsed.
/// Used for optional messages such as validation errors.
/// </summary>
public sealed class VisibleWhenNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || (value is string s && string.IsNullOrWhiteSpace(s))
            ? Visibility.Collapsed
            : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
