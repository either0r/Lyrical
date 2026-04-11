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
    private enum ConflictResolution
    {
        Reload,
        Overwrite,
        SaveAsCopy
    }

    private SongDocument _song = SongDocument.CreateNew();
    private PreviewWindow? _previewWindow;

    private readonly DispatcherTimer _autoSaveTimer = new();
    private bool _hasPendingChanges;
    private bool _isAutoSaving;
    private bool _isInitializing;
    private bool _hasExternalConflict;

    public bool HasPendingChanges => _hasPendingChanges;

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
        UpdateSongHeader();
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
            ApplyMetadataFromChordPro(_song);
        }

        if (_isInitializing)
        {
            return;
        }

        _hasPendingChanges = true;
        _hasExternalConflict = false;
        UpdateSongHeader();

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

        var conflict = await SongStorageService.CheckForExternalChangeAsync(_song);
        if (conflict.HasConflict)
        {
            _hasExternalConflict = true;
            UpdateSongHeader();
            return;
        }

        _isAutoSaving = true;
        try
        {
            var saved = await SongStorageService.SaveSongSilentlyAsync(_song);
            if (saved)
            {
                _hasPendingChanges = false;
                _hasExternalConflict = false;
                UpdateSongHeader();
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
        var cursor = EditorTextBox.SelectionStart;
        InsertAtCursor("{soc}" + cursor + "\n{eoc}");
    }

    private void InsertMetadataButton_Click(object sender, RoutedEventArgs e)
    {
        var cursor = EditorTextBox.SelectionStart;
        InsertAtCursor("{subtitle: " + cursor + "}\n");
    }

    private void InsertVerseBlockButton_Click(object sender, RoutedEventArgs e)
    {
        var cursor = EditorTextBox.SelectionStart;
        InsertAtCursor("{sov}" + cursor + "\n{eov}");
    }

    private void InsertCapoButton_Click(object sender, RoutedEventArgs e)
    {
        var cursor = EditorTextBox.SelectionStart;
        InsertAtCursor("{capo: " + cursor + "}\n");
    }

    private void InsertTabBlockButton_Click(object sender, RoutedEventArgs e)
    {
        InsertAtCursor("{sot}\ne|----------------|\nB|----------------|\nG|----------------|\nD|----------------|\nA|----------------|\nE|----------------|\n{eot}\n");
    }

    private void InsertMoreTabBlockButton_Click(object sender, RoutedEventArgs e)
    {
        var text = EditorTextBox.Text;
        var cursorPos = EditorTextBox.SelectionStart;

        // Normalize line endings – WinUI TextBox uses \r internally.
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // Find the {sot}…{eot} block that contains or is nearest before the cursor.
        int bestSot = -1;
        int bestEot = -1;
        int searchFrom = 0;

        while (true)
        {
            var sotIndex = normalized.IndexOf("{sot}", searchFrom, StringComparison.OrdinalIgnoreCase);
            if (sotIndex < 0)
            {
                break;
            }

            var eotIndex = normalized.IndexOf("{eot}", sotIndex, StringComparison.OrdinalIgnoreCase);
            if (eotIndex < 0)
            {
                break;
            }

            if (sotIndex <= cursorPos)
            {
                bestSot = sotIndex;
                bestEot = eotIndex;
            }

            searchFrom = eotIndex + 5;
        }

        if (bestSot < 0 || bestEot < 0)
        {
            return;
        }

        var blockStart = normalized.IndexOf('\n', bestSot);
        if (blockStart < 0 || blockStart >= bestEot)
        {
            return;
        }

        blockStart++;

        var blockContent = normalized[blockStart..bestEot];
        var lines = blockContent.Split('\n');
        const string extension = "----------------|";

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains('|'))
            {
                lines[i] = lines[i] + extension;
            }
        }

        var newBlock = string.Join('\n', lines);
        var result = normalized[..blockStart] + newBlock + normalized[bestEot..];
        EditorTextBox.Text = result;
        EditorTextBox.SelectionStart = blockStart + newBlock.Length;
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
        _ = MainWindow.Instance?.CloseCurrentTabAsync();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _ = MainWindow.Instance?.CloseCurrentTabAsync();
    }

    private async System.Threading.Tasks.Task SaveSongAsync(bool showConfirmation = true)
    {
        var conflict = await SongStorageService.CheckForExternalChangeAsync(_song);
        bool saved;

        if (conflict.HasConflict)
        {
            _hasExternalConflict = true;
            UpdateSongHeader();

            var resolution = await ShowConflictDialogAsync(conflict);
            if (resolution == ConflictResolution.Reload)
            {
                ApplyReloadFromConflict(conflict);
                return;
            }

            saved = resolution == ConflictResolution.SaveAsCopy
                ? await SongStorageService.SaveSongAsCopyAsync(_song)
                : await SongStorageService.SaveSongAsync(_song);
        }
        else
        {
            saved = await SongStorageService.SaveSongAsync(_song);
        }

        if (saved)
        {
            _hasPendingChanges = false;
            _hasExternalConflict = false;
            _autoSaveTimer.Stop();
        }

        UpdateSongHeader();

        if (showConfirmation)
        {
            var folderDisplay = string.IsNullOrWhiteSpace(_song.RelativeFolderPath) ? "Library Root" : _song.RelativeFolderPath;
            var saveTarget = string.IsNullOrWhiteSpace(_song.FileName) ? folderDisplay : $"{folderDisplay}\\{_song.FileName}";
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = saved ? "Song saved" : "Save cancelled",
                Content = saved
                    ? $"Saved to {saveTarget}."
                    : "No folder was selected. Save was cancelled.",
                CloseButtonText = "OK"
            };

            await dialog.ShowAsync();
        }
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
            UpdateSongHeader();
            return;
        }

        if (e.PropertyName == nameof(SongDocument.ChordDiagramPlacement))
        {
            DiagramPlacementComboBox.SelectedIndex = _song.ChordDiagramPlacement == ChordDiagramPlacement.Top ? 0 : 1;
            ApplyDiagramPlacement();
            return;
        }

        if (e.PropertyName is nameof(SongDocument.Title)
            or nameof(SongDocument.FileName)
            or nameof(SongDocument.RelativeFolderPath)
            or nameof(SongDocument.LastModified))
        {
            UpdateSongHeader();
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

    private void SetCursorHere()
    {
        var selectionStart = EditorTextBox.SelectionStart;
        
    }

    private void UnsubscribeSong()
    {
        _song.PropertyChanged -= Song_PropertyChanged;
    }

    private async System.Threading.Tasks.Task<ConflictResolution> ShowConflictDialogAsync(SongSaveConflictCheckResult conflict)
    {
        var content = conflict.IsMissing
            ? "This song file no longer exists in the shared folder. Choose Reload (discard local edits), Overwrite (recreate this file), or Save as copy."
            : $"This file was modified by someone else at {conflict.CurrentFileLastModified?.LocalDateTime:M/dd/yyyy h:mm tt}. Choose Reload, Overwrite, or Save as copy.";

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Save conflict detected",
            Content = content,
            PrimaryButtonText = "Reload",
            SecondaryButtonText = "Overwrite",
            CloseButtonText = "Save as copy",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => ConflictResolution.Reload,
            ContentDialogResult.Secondary => ConflictResolution.Overwrite,
            _ => ConflictResolution.SaveAsCopy
        };
    }

    private void ApplyReloadFromConflict(SongSaveConflictCheckResult conflict)
    {
        if (!conflict.IsMissing && !string.IsNullOrWhiteSpace(conflict.CurrentFileContent))
        {
            _song.ChordPro = conflict.CurrentFileContent;
        }

        if (conflict.CurrentFileLastModified is DateTimeOffset modified)
        {
            _song.LastModified = modified;
        }

        _hasPendingChanges = false;
        _hasExternalConflict = false;
        _autoSaveTimer.Stop();
        UpdateSongHeader();
    }

    private async void SaveSongButton_Click(object sender, RoutedEventArgs e)
    {
        await SaveSongAsync();
    }

    private void SetHelpTextBlock(object sender, RoutedEventArgs e)
    {
        var helpTextButtonFlyout = HelpTextBlock;
        var text = "Directives\nDirectives should be placed on their own line.\n\rMeta-Data Directives\ntitle, subtitle, artist, album, year, key, tempo, capo.\n{title: Song Title}\n{subtitle: Song Subtitle}\n\nFormatting Directives\n" +
            "comment, comment_italic\n{comment: some comment here}\n {comment_italic: some comment in italic}\n\nEnvironment Directives\nEnvironment directives always come in pairs, one to start the environment and one to end it.\n" +
            "Environment Directives can also use labels to provide more detail about that section\n" +
            "{sov: Verse 1}\n" +
            "start_of_chorus (short: soc), end_of_chorus (short: eoc)\nchorus, eoc\n" +
            "start_of_verse (short: sov), end_of_verse (short: eov)\n" +
            "start_of_bridge (short: sob) end_of_bridge (short: eob)\n";
        HelpTextBlock.Text = text;
    }

    private async void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_song.FileName))
        {
            return;
        }

        var backups = await BackupService.GetBackupsAsync(_song.RelativeFilePath);
        if (backups.Count == 0)
        {
            var emptyDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "No backups",
                Content = "No backup history found for this song.",
                CloseButtonText = "OK"
            };
            await emptyDialog.ShowAsync();
            return;
        }

        var selectedBackup = await ShowBackupSelectionAsync(backups);
        if (selectedBackup is null)
        {
            return;
        }

        var restoredContent = await BackupService.RestoreBackupAsync(selectedBackup.FileName);
        if (string.IsNullOrWhiteSpace(restoredContent))
        {
            return;
        }

        _isInitializing = true;
        _song.ChordPro = restoredContent;
        ApplyMetadataFromChordPro(_song);
        EditorTextBox.Text = _song.ChordPro;
        RefreshPreview();
        _isInitializing = false;

        _hasPendingChanges = true;
        _hasExternalConflict = false;
        UpdateSongHeader();

        var confirmDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Backup restored",
            Content = $"Restored from {selectedBackup.Timestamp}. You can now save or continue editing.",
            PrimaryButtonText = "Save",
            CloseButtonText = "Keep editing",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await confirmDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            SaveSongButton_Click(sender, e);
        }
    }

    private async System.Threading.Tasks.Task<Services.BackupInfo?> ShowBackupSelectionAsync(IReadOnlyList<Services.BackupInfo> backups)
    {
        var items = backups.Select(b => $"{b.Index}. {b.Timestamp}").ToList();

        var listBox = new ListBox
        {
            ItemsSource = items,
            Height = 200,
            SelectedIndex = 0
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Restore backup",
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Select a backup to restore:", Margin = new Thickness(0, 0, 0, 12) },
                    listBox
                }
            },
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel"
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        var selectedIndex = listBox.SelectedIndex;
        return selectedIndex >= 0 && selectedIndex < backups.Count ? backups[selectedIndex] : null;
    }

    private void ApplyMetadataFromChordPro(SongDocument song)
    {
        var lines = song.ChordPro.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
            {
                continue;
            }

            var inner = trimmed[1..^1];
            var colonIdx = inner.IndexOf(':');
            if (colonIdx < 0)
            {
                continue;
            }

            var directiveName = inner[..colonIdx].Trim().ToLowerInvariant();
            var directiveValue = inner[(colonIdx + 1)..].Trim();

            if ((directiveName == "title" || directiveName == "t") && !string.IsNullOrWhiteSpace(directiveValue))
            {
                song.Title = directiveValue;
            }

            if ((directiveName == "artist" || directiveName == "subtitle") && !string.IsNullOrWhiteSpace(directiveValue))
            {
                song.Artist = directiveValue;
            }

            if (directiveName == "key" && !string.IsNullOrWhiteSpace(directiveValue))
            {
                song.Key = directiveValue;
            }

            if (directiveName == "x_creator" && !string.IsNullOrWhiteSpace(directiveValue))
            {
                song.CreatedBy = directiveValue;
            }
        }
    }

    private void UpdateSongHeader()
    {
        SongTitleText.Text = string.IsNullOrWhiteSpace(_song.Title)
            ? "Untitled"
            : _song.Title;

        var folderDisplay = string.IsNullOrWhiteSpace(_song.RelativeFolderPath)
            ? "Library Root"
            : _song.RelativeFolderPath;

        SongLocationText.Text = string.IsNullOrWhiteSpace(_song.FileName)
            ? $"New song in {folderDisplay}"
            : $"{folderDisplay} • {_song.FileName}";

        SongStatusText.Text = _hasExternalConflict
            ? "Shared changes detected"
            : _hasPendingChanges
                ? "Unsaved changes"
                : string.IsNullOrWhiteSpace(_song.FileName)
                    ? "Not saved yet"
                    : $"Saved { _song.LastModifiedText}";

        MainWindow.Instance?.UpdateCurrentTabDirtyIndicator();
    }

    public async System.Threading.Tasks.Task TriggerSaveAsync()
    {
        await SaveSongAsync(showConfirmation: false);
    }

    public void TriggerSave()
    {
        SaveSongButton_Click(null!, new RoutedEventArgs());
    }

    public sealed record BackupInfo(string FileName, string Timestamp, int Index);
    private sealed class DatamuseWordResult
    {
        [JsonPropertyName("word")]
        public string? Word { get; set; }
    }
}
