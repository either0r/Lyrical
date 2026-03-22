using Lyrical.Models;
using Lyrical.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lyrical.Pages;

public sealed partial class SongEditorPage : Page
{
    private SongDocument _song = SongDocument.CreateNew();
    private PreviewWindow? _previewWindow;

    private readonly DispatcherTimer _autoSaveTimer = new();
    private bool _hasPendingChanges;
    private bool _isAutoSaving;
    private bool _isInitializing;

    private static readonly HttpClient _httpClient = new();
    private int _searchRequestVersion;

    private const double MinPaneWidth = 260;
    private const double DefaultEditorFontSize = 16;

    public SongEditorPage()
    {
        InitializeComponent();

        _autoSaveTimer.Tick += AutoSaveTimer_Tick;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        UnsubscribeSong();

        if (e.Parameter is SongDocument song)
        {
            _song = song;
        }

        _isInitializing = true;
        _song.PropertyChanged += Song_PropertyChanged;
        EditorTextBox.Text = _song.ChordPro;
        DiagramPlacementComboBox.SelectedIndex = _song.ChordDiagramPlacement == ChordDiagramPlacement.Top ? 0 : 1;
        FontSizeComboBox.SelectedIndex = 2;
        ApplyEditorFontSize(DefaultEditorFontSize);
        RefreshPreview();
        _isInitializing = false;

        _hasPendingChanges = false;
        _autoSaveTimer.Stop();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _autoSaveTimer.Stop();
        UnsubscribeSong();
        base.OnNavigatedFrom(e);
    }

    private void EditorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_song.ChordPro != EditorTextBox.Text)
        {
            _song.ChordPro = EditorTextBox.Text;
        }

        if (_isInitializing)
        {
            return;
        }

        _hasPendingChanges = true;
        if (EditorSettingsService.AutoSaveMode == AutoSaveMode.AfterDelay)
        {
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(EditorSettingsService.AutoSaveDelaySeconds);
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }
        else
        {
            _autoSaveTimer.Stop();
        }
    }

    private async void EditorTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (EditorSettingsService.AutoSaveMode == AutoSaveMode.OnFocusChange)
        {
            await TryAutoSaveAsync();
        }
    }

    private async void AutoSaveTimer_Tick(object? sender, object e)
    {
        _autoSaveTimer.Stop();
        if (EditorSettingsService.AutoSaveMode == AutoSaveMode.AfterDelay)
        {
            await TryAutoSaveAsync();
        }
    }

    private async System.Threading.Tasks.Task TryAutoSaveAsync()
    {
        if (_isAutoSaving || !_hasPendingChanges)
        {
            return;
        }

        _isAutoSaving = true;
        try
        {
            var saved = await SongStorageService.SaveSongSilentlyAsync(_song);
            if (saved)
            {
                _hasPendingChanges = false;
            }
        }
        finally
        {
            _isAutoSaving = false;
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

    private void FontSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontSizeComboBox.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && double.TryParse(tag, out var size))
        {
            ApplyEditorFontSize(size);
        }
    }

    private void ApplyEditorFontSize(double size)
    {
        EditorTextBox.FontSize = size;
        PreviewRichTextBlock.FontSize = size;
    }

    private void EditorPreviewSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var left = EditorTextBox.ActualWidth + e.HorizontalChange;
        var right = PreviewPaneScrollViewer.ActualWidth - e.HorizontalChange;

        if (left < MinPaneWidth)
        {
            left = MinPaneWidth;
            right = EditorPreviewGrid.ActualWidth - left - 8;
        }

        if (right < MinPaneWidth)
        {
            right = MinPaneWidth;
            left = EditorPreviewGrid.ActualWidth - right - 8;
        }

        if (left < MinPaneWidth || right < MinPaneWidth)
        {
            return;
        }

        EditorColumn.Width = new GridLength(left, GridUnitType.Pixel);
        PreviewColumn.Width = new GridLength(right, GridUnitType.Pixel);
    }

    private void InsertChorusBlockButton_Click(object sender, RoutedEventArgs e)
    {
        InsertAtCursor("{soc}\n");
    }

    private void InsertMetadataButton_Click(object sender, RoutedEventArgs e)
    {
        InsertAtCursor("{subtitle: }\n");
    }

    private void InsertVerseBlockButton_Click(object sender, RoutedEventArgs e)
    {
        InsertAtCursor("{sov}\n");
    }

    private void InsertCapoButton_Click(object sender, RoutedEventArgs e)
    {
        InsertAtCursor("{capo: }\n");
    }

    private void InsertTabBlockButton_Click(object sender, RoutedEventArgs e)
    {
        InsertAtCursor("{sot}\n\n{eot}\n");
    }

    private async void SimilarSoundSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        var query = sender.Text?.Trim() ?? string.Empty;
        if (query.Length < 2)
        {
            sender.ItemsSource = null;
            return;
        }

        var requestVersion = ++_searchRequestVersion;
        var results = await SearchSimilarSoundingWordsAsync(query);

        if (requestVersion != _searchRequestVersion)
        {
            return;
        }

        sender.ItemsSource = results;
    }

    private void SimilarSoundSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var selectedWord = args.ChosenSuggestion as string;
        if (string.IsNullOrWhiteSpace(selectedWord))
        {
            selectedWord = sender.Text?.Trim();
        }

        if (!string.IsNullOrWhiteSpace(selectedWord))
        {
            InsertAtCursor(selectedWord + " ");
        }
    }

    private static async System.Threading.Tasks.Task<IReadOnlyList<string>> SearchSimilarSoundingWordsAsync(string query)
    {
        try
        {
            var url = $"https://api.datamuse.com/words?rel_rhy={Uri.EscapeDataString(query)}&max=12";
            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            var payload = await JsonSerializer.DeserializeAsync<List<DatamuseWordResult>>(stream);
            if (payload is null)
            {
                return [];
            }

            return payload
                .Select(p => p.Word?.Trim())
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private void CloseCurrentDocumentButton_Click(object sender, RoutedEventArgs e)
    {

    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame?.CanGoBack == true)
        {
            Frame.GoBack();
            return;
        }

        Frame?.Navigate(typeof(SongListPage));
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSongButton_Click(sender, e);
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
        if (saved)
        {
            _hasPendingChanges = false;
            _autoSaveTimer.Stop();
        }

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

    private void SetHelpTextBlock(object sender, RoutedEventArgs e)
    {
        var helpTextButtonFlyout = HelpTextBlock;
        var text = "Directives\nDirectives should be placed on their own line.\n\rMeta-Data Directives\ntitle, subtitle, artist, album, year, key, tempo, capo.\n{title: Song Title}\n{subtitle: Song Subtitle}\n\nFormatting Directives\n" +
            "comment, comment_italic\n{comment: some comment here}\n {comment_italic: some comment in italic}\n\nEnvironment Directives\nEnvironment directives always come in pairs, one to start the environment and one to end it.\n" +
            "start_of_chorus (short: soc), end_of_chorus (short: eoc)\nchorus\n" +
            "start_of_verse (short: sov), end_of_verse (short: eov)\n" +
            "start_of_bridge (short: sob) end_of_bridge (short: eob)\n";
        HelpTextBlock.Text = text;
    }

    private sealed class DatamuseWordResult
    {
        [JsonPropertyName("word")]
        public string? Word { get; set; }
    }
}
