using Lyrical.Models;
using Lyrical.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lyrical;

public sealed class PreviewWindow : Window
{
    public PreviewWindow(SongDocument song)
    {
        Title = "Song Preview";

        var frame = new Frame();
        Content = frame;
        frame.Navigate(typeof(PreviewPage), new PreviewNavigationContext
        {
            Song = song,
            ShowBackButton = false,
            CloseAction = Close
        });
    }
}
