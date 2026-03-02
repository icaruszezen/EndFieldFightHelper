using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EndFieldFightHelper.Converters;

public class ActionTypeToBrushConverter : IValueConverter
{
    public static readonly ActionTypeToBrushConverter Instance = new();

    private static readonly IBrush AttackBrush = new SolidColorBrush(Color.Parse("#78909C"));
    private static readonly IBrush SkillBrush = new SolidColorBrush(Color.Parse("#42A5F5"));
    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.Parse("#66BB6A"));
    private static readonly IBrush UltimateBrush = new SolidColorBrush(Color.Parse("#FFA726"));
    private static readonly IBrush FallbackBrush = new SolidColorBrush(Color.Parse("#888888"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string type ? type switch
        {
            "attack" => AttackBrush,
            "skill" => SkillBrush,
            "link" => LinkBrush,
            "ultimate" => UltimateBrush,
            _ => FallbackBrush,
        } : FallbackBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
