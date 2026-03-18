using Lyrical.Models;
using Lyrical.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.ComponentModel;

namespace Lyrical.Pages;

public sealed partial class SongEditorPage : Page
{
    private SongDocument _song = SongDocument.CreateNew();
    private PreviewWindow? _previewWindow;

    public SongEditorPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        UnsubscribeSong();

        if (e.Parameter is SongDocument song)
        {
            _song = song;
        }

        _song.PropertyChanged += Song_PropertyChanged;
        EditorTextBox.Text = _song.ChordPro;
        DiagramPlacementComboBox.SelectedIndex = _song.ChordDiagramPlacement == ChordDiagramPlacement.Top ? 0 : 1;
        RefreshPreview();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        UnsubscribeSong();
        base.OnNavigatedFrom(e);
    }

    private void EditorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_song.ChordPro != EditorTextBox.Text)
        {
            _song.ChordPro = EditorTextBox.Text;
        }
    }

    private void DiagramPlacementComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = DiagramPlacementComboBox.SelectedIndex == 0
            ? ChordDiagramPlacement.Top
            : ChordDiagramPlacement.Bottom;

        if (_song.ChordDiagramPlacement != selected)
        {
            _song.ChordDiagramPlacement = selected;
        }
    }

    private void InsertChorusBlockButton_Click(object sender, RoutedEventArgs e)
    {
        InsertAtCursor("\n{soc}\n[C]Chorus line\n{eoc}\n");
    }

    private void InsertMetadataButton_Click(object sender, RoutedEventArgs e)
    {
        InsertAtCursor("\n{title: Song Title}\n{artist: Artist Name}\n{key: C}\n");
    }

    private void CloseCurrentDocumentButton_Click(object sender, RoutedEventArgs e)
    {

    }

    private void PopOutPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_previewWindow is null)
        {
            _previewWindow = new PreviewWindow(_song);
            _previewWindow.Closed += PreviewWindow_Closed;
        }

        _previewWindow.Activate();
    }

    private void PreviewWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_previewWindow is not null)
        {
            _previewWindow.Closed -= PreviewWindow_Closed;
            _previewWindow = null;
        }
    }

    private void Song_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SongDocument.ChordPro))
        {
            if (EditorTextBox.Text != _song.ChordPro)
            {
                EditorTextBox.Text = _song.ChordPro;
                EditorTextBox.SelectionStart = EditorTextBox.Text.Length;
            }

            RefreshPreview();
            return;
        }

        if (e.PropertyName == nameof(SongDocument.ChordDiagramPlacement))
        {
            DiagramPlacementComboBox.SelectedIndex = _song.ChordDiagramPlacement == ChordDiagramPlacement.Top ? 0 : 1;
            ApplyDiagramPlacement();
        }
    }

    private void RefreshPreview()
    {
        ChordProRenderer.RenderTo(PreviewRichTextBlock, _song.ChordPro);
        RenderDiagrams();
        ApplyDiagramPlacement();
    }

    private void RenderDiagrams()
    {
        ChordDiagramPanel.Children.Clear();

        foreach (var chord in ChordDiagramRenderer.ExtractChords(_song.ChordPro))
        {
            ChordDiagramPanel.Children.Add(ChordDiagramRenderer.CreateDiagramCard(chord));
        }

        ChordDiagramScrollViewer.Visibility = ChordDiagramPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyDiagramPlacement()
    {
        var top = _song.ChordDiagramPlacement == ChordDiagramPlacement.Top;
        Grid.SetRow(ChordDiagramScrollViewer, top ? 0 : 1);
        Grid.SetRow(PreviewRichTextBlock, top ? 1 : 0);
    }

    private void InsertAtCursor(string text)
    {
        var selectionStart = EditorTextBox.SelectionStart;
        var current = EditorTextBox.Text;
        EditorTextBox.Text = current.Insert(selectionStart, text);
        EditorTextBox.SelectionStart = selectionStart + text.Length;
    }

    private void UnsubscribeSong()
    {
        _song.PropertyChanged -= Song_PropertyChanged;
    }

    private async void SaveSongButton_Click(object sender, RoutedEventArgs e)
    {
        var saved = await SongStorageService.SaveSongAsync(_song);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = saved ? "Song saved" : "Save cancelled",
            Content = saved
                ? "Your song was saved. It will appear in Songs list."
                : "No folder was selected. Save was cancelled.",
            CloseButtonText = "OK"
        };

        await dialog.ShowAsync();
    }
}
