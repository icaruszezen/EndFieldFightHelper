using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EndFieldFightHelper.Converters;

public class RarityToBrushConverter : IValueConverter
{
    public static readonly RarityToBrushConverter Instance = new();

    private static readonly IBrush Rarity6 = new SolidColorBrush(Color.Parse("#FFAB40"));
    private static readonly IBrush Rarity5 = new SolidColorBrush(Color.Parse("#CE93D8"));
    private static readonly IBrush Rarity4 = new SolidColorBrush(Color.Parse("#64B5F6"));
    private static readonly IBrush Fallback = new SolidColorBrush(Color.Parse("#888888"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is int rarity ? rarity switch
        {
            6 => Rarity6,
            5 => Rarity5,
            4 => Rarity4,
            _ => Fallback,
        } : Fallback;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
