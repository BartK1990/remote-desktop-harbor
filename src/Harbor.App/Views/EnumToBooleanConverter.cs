using System.Globalization;
using System.Windows.Data;

namespace Harbor.App.Views;

/// <summary>
/// Binds a group of radio buttons to one enum value: checked when the value equals the parameter,
/// and checking a button writes its parameter back. Used by the theme choice in Settings → Appearance (SPEC FR-08).
/// </summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value, parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}
