using Lyrical.Models;
using Lyrical.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.ComponentModel;

namespace Lyrical.Pages;

public sealed partial class PreviewPage : Page
{
    private SongDocument? _song;
    private Action? _closeAction;

    public PreviewPage()
    {
        InitializeComponent();
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

        if (_song is not null)
        {
            _song.PropertyChanged += Song_PropertyChanged;
            RefreshPreview();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
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

    private void UnsubscribeSong()
    {
        if (_song is not null)
        {
            _song.PropertyChanged -= Song_PropertyChanged;
        }

        _song = null;
    }
}
