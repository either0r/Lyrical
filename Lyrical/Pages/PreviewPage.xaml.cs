using Lyrical.Models;
using Lyrical.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.ComponentModel;

namespace Lyrical.Pages;

public sealed partial class PreviewPage : Page
{
    private SongDocument? _song;
    private Action? _closeAction;
    private readonly DispatcherTimer _autoScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private double _autoScrollMultiplier = 1.0;
    private bool _isAutoScrolling;

    public PreviewPage()
    {
        InitializeComponent();
        _autoScrollTimer.Tick += AutoScrollTimer_Tick;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        UnsubscribeSong();

        if (e.Parameter is PreviewNavigationContext context)
        {
            _song = context.Song;
            _closeAction = context.CloseAction;
        }
        else if (e.Parameter is SongDocument song)
        {
            _song = song;
            _closeAction = null;
        }

        AutoScrollControlsPanel.Visibility = _closeAction is not null ? Visibility.Visible : Visibility.Collapsed;

        if (_song is not null)
        {
            _song.PropertyChanged += Song_PropertyChanged;
            RefreshPreview();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        StopAutoScroll();
        UnsubscribeSong();
        base.OnNavigatedFrom(e);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        StopAutoScroll();

        if (_closeAction is not null)
        {
            _closeAction.Invoke();
            return;
        }

        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void Song_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SongDocument.ChordPro) || e.PropertyName == nameof(SongDocument.ChordDiagramPlacement))
        {
            RefreshPreview();
        }
    }

    private void RefreshPreview()
    {
        if (_song is null)
        {
            return;
        }

        ChordProRenderer.RenderTo(PreviewRichTextBlock, _song.ChordPro);

        ChordDiagramPanel.Children.Clear();
        foreach (var chord in ChordDiagramRenderer.ExtractChords(_song.ChordPro))
        {
            ChordDiagramPanel.Children.Add(ChordDiagramRenderer.CreateDiagramCard(chord));
        }

        ChordDiagramScrollViewer.Visibility = ChordDiagramPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var top = _song.ChordDiagramPlacement == ChordDiagramPlacement.Top;
        Grid.SetRow(ChordDiagramScrollViewer, top ? 0 : 1);
        Grid.SetRow(PreviewRichTextBlock, top ? 1 : 0);
    }

    private void AutoScrollButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isAutoScrolling)
        {
            StopAutoScroll();
            return;
        }

        _isAutoScrolling = true;
        AutoScrollButton.Content = "Stop Auto-scroll";
        _autoScrollTimer.Start();
    }

    private void AutoScrollSpeedSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        _autoScrollMultiplier = e.NewValue;
        if (AutoScrollSpeedText is not null)
        {
            AutoScrollSpeedText.Text = $"{_autoScrollMultiplier:0.#}x";
        }
    }

    private void AutoScrollTimer_Tick(object? sender, object e)
    {
        if (PreviewScrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        var delta = 1.2 * _autoScrollMultiplier;
        var nextOffset = PreviewScrollViewer.VerticalOffset + delta;

        if (nextOffset >= PreviewScrollViewer.ScrollableHeight)
        {
            PreviewScrollViewer.ChangeView(null, PreviewScrollViewer.ScrollableHeight, null, true);
            StopAutoScroll();
            return;
        }

        PreviewScrollViewer.ChangeView(null, nextOffset, null, true);
    }

    private void PreviewScrollViewer_UserScrollDetected(object sender, PointerRoutedEventArgs e)
    {
        if (_isAutoScrolling)
        {
            StopAutoScroll();
        }
    }

    private void StopAutoScroll()
    {
        _autoScrollTimer.Stop();
        _isAutoScrolling = false;
        AutoScrollButton.Content = "Start Auto-scroll";
    }

    private void UnsubscribeSong()
    {
        if (_song is not null)
        {
            _song.PropertyChanged -= Song_PropertyChanged;
        }

        _song = null;
    }
}
