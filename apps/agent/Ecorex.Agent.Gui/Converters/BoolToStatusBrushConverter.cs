using System;
using System.Globalization;
using System.Windows.Data;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;

namespace Ecorex.Agent.Gui.Converters;

/// <summary>
/// true (en linea) -> verde tenue; false (offline/conectando) -> gris. Unico punto de "color" del
/// look monocromo: el semaforo de estado.
/// </summary>
public sealed class BoolToStatusBrushConverter : IValueConverter
{
    private static readonly Brush Online = new SolidColorBrush(Color.FromRgb(0x8F, 0xE3, 0xA2));
    private static readonly Brush Offline = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x93));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Online : Offline;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
