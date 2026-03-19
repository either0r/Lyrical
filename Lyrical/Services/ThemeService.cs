using Microsoft.UI.Xaml;
using System;
using Windows.Storage;

namespace Lyrical.Services;

public static class ThemeService
{
    private const string SettingsKey = "AppTheme";

    public static ElementTheme Current { get; private set; } = ElementTheme.Default;

    public static event Action<ElementTheme>? ThemeChanged;

    public static void Load()
    {
        if (ApplicationData.Current.LocalSettings.Values[SettingsKey] is string saved
            && Enum.TryParse<ElementTheme>(saved, out var theme))
        {
            Current = theme;
        }
        else
        {
            Current = ElementTheme.Default;
        }
    }

    public static void Apply(ElementTheme theme)
    {
        Current = theme;
        ApplicationData.Current.LocalSettings.Values[SettingsKey] = theme.ToString();

        if (App.MainAppWindow?.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }

        ThemeChanged?.Invoke(theme);
    }
}
