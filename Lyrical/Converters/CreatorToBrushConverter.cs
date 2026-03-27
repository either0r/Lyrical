using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;

namespace Lyrical.Converters;

public sealed class CreatorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var creator = value as string;
        if (string.IsNullOrWhiteSpace(creator))
        {
            return new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
        }

        var hash = Math.Abs(creator.GetHashCode(StringComparison.OrdinalIgnoreCase));
        var hue = hash % 360;
        var color = FromHsl(hue / 360.0, 0.45, 0.22);
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }

    private static Color FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs((h * 6 % 2) - 1));
        var m = l - c / 2;

        double r1, g1, b1;
        if (h < 1.0 / 6) (r1, g1, b1) = (c, x, 0);
        else if (h < 2.0 / 6) (r1, g1, b1) = (x, c, 0);
        else if (h < 3.0 / 6) (r1, g1, b1) = (0, c, x);
        else if (h < 4.0 / 6) (r1, g1, b1) = (0, x, c);
        else if (h < 5.0 / 6) (r1, g1, b1) = (x, 0, c);
        else (r1, g1, b1) = (c, 0, x);

        byte r = (byte)Math.Round((r1 + m) * 255);
        byte g = (byte)Math.Round((g1 + m) * 255);
        byte b = (byte)Math.Round((b1 + m) * 255);

        return Color.FromArgb(255, r, g, b);
    }
}
