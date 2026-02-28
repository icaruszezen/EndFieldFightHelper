using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EndFieldFightHelper.Converters;

public class BoolToActiveBrushConverter : IValueConverter
{
    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#FF9800"));
    private static readonly IBrush InactiveBrush = Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? ActiveBrush : InactiveBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
