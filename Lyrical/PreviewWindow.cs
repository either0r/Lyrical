using Lyrical.Models;
using Lyrical.Pages;
using Lyrical.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Graphics;
using Windows.Storage;
using WinRT.Interop;

namespace Lyrical;

public sealed class PreviewWindow : Window
{
    private const string XKey = "PreviewWindowX";
    private const string YKey = "PreviewWindowY";
    private const string WidthKey = "PreviewWindowWidth";
    private const string HeightKey = "PreviewWindowHeight";

    private const int DefaultWidth = 960;
    private const int DefaultHeight = 760;

    private bool _placementApplied;

    public PreviewWindow(SongDocument song)
    {
        Title = "Song Preview";
        SystemBackdrop = new MicaBackdrop();

        AppWindow.SetIcon("Assets/StoreLogo.scale-125.ico");

        var frame = new Frame();
        Content = frame;

        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = ThemeService.Current;
        }

        ApplyTitleBarTheme(ThemeService.Current);

        frame.Navigate(typeof(PreviewPage), new PreviewNavigationContext
        {
            Song = song,
            ShowBackButton = false,
            CloseAction = Close
        });

        ThemeService.ThemeChanged += OnThemeChanged;
        Activated += PreviewWindow_Activated;
        Closed += PreviewWindow_Closed;
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }

        ApplyTitleBarTheme(theme);
    }

    private void ApplyTitleBarTheme(ElementTheme theme)
    {
        AppWindow.TitleBar.PreferredTheme = theme switch
        {
            ElementTheme.Light => TitleBarTheme.Light,
            ElementTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode
        };
    }

    private void PreviewWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_placementApplied)
        {
            return;
        }

        _placementApplied = true;

        var appWindow = GetAppWindow(this);
        if (appWindow is null)
        {
            return;
        }

        if (TryLoadLastPlacement(out var rect))
        {
            appWindow.MoveAndResize(rect);
            return;
        }

        var fallback = GetDefaultPlacementOnMainDisplay();
        appWindow.MoveAndResize(fallback);
    }

    private void PreviewWindow_Closed(object sender, WindowEventArgs args)
    {
        ThemeService.ThemeChanged -= OnThemeChanged;

        var appWindow = GetAppWindow(this);
        if (appWindow is null)
        {
            return;
        }

        var settings = ApplicationData.Current.LocalSettings.Values;
        settings[XKey] = appWindow.Position.X;
        settings[YKey] = appWindow.Position.Y;
        settings[WidthKey] = appWindow.Size.Width;
        settings[HeightKey] = appWindow.Size.Height;
    }

    private static bool TryLoadLastPlacement(out RectInt32 rect)
    {
        var values = ApplicationData.Current.LocalSettings.Values;

        if (values[XKey] is int x
            && values[YKey] is int y
            && values[WidthKey] is int width
            && values[HeightKey] is int height
            && width > 200
            && height > 200)
        {
            rect = new RectInt32(x, y, width, height);
            return true;
        }

        rect = default;
        return false;
    }

    private static RectInt32 GetDefaultPlacementOnMainDisplay()
    {
        var mainWindow = App.MainAppWindow;
        var mainAppWindow = mainWindow is null ? null : GetAppWindow(mainWindow);

        if (mainAppWindow is null)
        {
            return new RectInt32(100, 100, DefaultWidth, DefaultHeight);
        }

        var mainWindowId = mainAppWindow.Id;
        var displayArea = DisplayArea.GetFromWindowId(mainWindowId, DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;

        var width = Math.Min(DefaultWidth, workArea.Width);
        var height = Math.Min(DefaultHeight, workArea.Height);

        var suggestedX = mainAppWindow.Position.X + 40;
        var suggestedY = mainAppWindow.Position.Y + 40;

        var maxX = workArea.X + workArea.Width - width;
        var maxY = workArea.Y + workArea.Height - height;

        var x = Math.Clamp(suggestedX, workArea.X, maxX);
        var y = Math.Clamp(suggestedY, workArea.Y, maxY);

        return new RectInt32(x, y, width, height);
    }

    private static AppWindow? GetAppWindow(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }
}
