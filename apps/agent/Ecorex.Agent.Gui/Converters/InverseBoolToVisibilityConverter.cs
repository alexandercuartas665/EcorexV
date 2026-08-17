using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Ecorex.Agent.Gui.Converters;

/// <summary>Visible cuando el bool es FALSE (inverso de BooleanToVisibility). Se usa para ocultar el boton
/// "Quitar" en los perfiles de login Builtin (que no se pueden quitar).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v != Visibility.Visible;
}
