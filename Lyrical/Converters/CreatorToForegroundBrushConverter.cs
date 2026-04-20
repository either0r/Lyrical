using Lyrical.Services;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace Lyrical.Converters;

public sealed class CreatorToForegroundBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var creator = value as string;
        return new SolidColorBrush(CreatorColorService.ResolveTextColor(creator));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
