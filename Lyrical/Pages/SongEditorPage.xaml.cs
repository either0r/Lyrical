using Lyrical.Models;
using Lyrical.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.System;

namespace Lyrical.Pages;

public sealed partial class SongEditorPage : Page
{
    public StandardUICommand UndoCommand { get; } = new(StandardUICommandKind.Undo);
    public StandardUICommand RedoCommand { get; } = new(StandardUICommandKind.Redo);
    public StandardUICommand CutCommand { get; } = new(StandardUICommandKind.Cut);
    public StandardUICommand CopyCommand { get; } = new(StandardUICommandKind.Copy);
    public StandardUICommand PasteCommand { get; } = new(StandardUICommandKind.Paste);
    public StandardUICommand SelectAllCommand { get; } = new(StandardUICommandKind.SelectAll);
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
    private const string CursorMarker = "$$";
    private const string EditorTabSpaces = "    ";
    private static readonly HashSet<string> MetadataDirectiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "t", "subtitle", "artist", "album", "year", "key", "tempo", "capo", "x_creator"
    };

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

    private void EditorTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Tab)
        {
            return;
        }

        e.Handled = true;

        var selectionStart = EditorTextBox.SelectionStart;
        var selectionLength = EditorTextBox.SelectionLength;
        var current = EditorTextBox.Text ?? string.Empty;

        if (selectionLength > 0)
        {
            current = current.Remove(selectionStart, selectionLength);
        }

        EditorTextBox.Text = current.Insert(selectionStart, EditorTabSpaces);
        EditorTextBox.SelectionStart = selectionStart + EditorTabSpaces.Length;
        EditorTextBox.SelectionLength = 0;
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
        EditorTextBox.Focus(FocusState.Programmatic);
        InsertAtCursorWithCaret("{soc}\n$$\n{eoc}");
    }

    private void InsertVerseBlockButton_Click(object sender, RoutedEventArgs e)
    {
        EditorTextBox.Focus(FocusState.Programmatic);
        InsertAtCursorWithCaret("{sov}\n$$\n{eov}");
    }

    private void InsertBridgeBlockButton_Click(object sender, RoutedEventArgs e)
    {
        EditorTextBox.Focus(FocusState.Programmatic);
        InsertAtCursorWithCaret("{sob}\n$$\n{eob}");
    }

    private void InsertCapoButton_Click(object sender, RoutedEventArgs e)
    {
        EditorTextBox.Focus(FocusState.Programmatic);

        var normalized = (EditorTextBox.Text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n').ToList();

        var lastMetadataLineIndex = -1;
        var sawBodyContent = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (IsMetadataDirectiveLine(trimmed) && !sawBodyContent)
            {
                lastMetadataLineIndex = i;
                continue;
            }

            sawBodyContent = true;
            if (lastMetadataLineIndex >= 0)
            {
                break;
            }
        }

        var insertLineIndex = lastMetadataLineIndex >= 0 ? lastMetadataLineIndex + 1 : 0;
        lines.Insert(insertLineIndex, "{capo: }");

        var updated = string.Join('\n', lines);
        EditorTextBox.Text = updated;

        var caretIndex = 0;
        for (var i = 0; i < insertLineIndex; i++)
        {
            caretIndex += lines[i].Length + 1;
        }

        caretIndex += "{capo: ".Length;
        EditorTextBox.SelectionStart = Math.Min(caretIndex, EditorTextBox.Text.Length);
    }

    private static bool IsMetadataDirectiveLine(string trimmedLine)
    {
        if (!trimmedLine.StartsWith('{') || !trimmedLine.EndsWith('}'))
        {
            return false;
        }

        var inner = trimmedLine[1..^1];
        var colonIndex = inner.IndexOf(':');
        if (colonIndex <= 0)
        {
            return false;
        }

        var directiveName = inner[..colonIndex].Trim();
        return MetadataDirectiveNames.Contains(directiveName);
    }

    private void InsertTabBlockButton_Click(object sender, RoutedEventArgs e)
    {
        EditorTextBox.Focus(FocusState.Programmatic);

        InsertAtCursor("\n{sot}\ne|----------------|\nB|----------------|\nG|----------------|\nD|----------------|\nA|----------------|\nE|----------------|\n{eot}\n");
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

    private void InsertAtCursorWithCaret(string textWithMarker)
    {
        var caretOffset = textWithMarker.IndexOf(CursorMarker, StringComparison.Ordinal);
        var textToInsert = caretOffset >= 0
            ? textWithMarker.Remove(caretOffset, CursorMarker.Length)
            : textWithMarker;

        var selectionStart = EditorTextBox.SelectionStart;
        var current = EditorTextBox.Text;
        EditorTextBox.Text = current.Insert(selectionStart, textToInsert);
        EditorTextBox.SelectionStart = selectionStart + (caretOffset >= 0 ? caretOffset : textToInsert.Length);
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

    private void DefineNewChordDiagram_Click(object sender, RoutedEventArgs e)
    {
        DefineNewChordDiagramButton_Click(sender, e);
    }

    private async void DefineNewChordDiagramButton_Click(object sender, RoutedEventArgs e)
    {
        var chordAtCaret = TryGetChordAtCaret();
        var existing = !string.IsNullOrWhiteSpace(chordAtCaret)
            ? CustomChordService.Definitions.FirstOrDefault(d => string.Equals(d.Name, chordAtCaret, StringComparison.OrdinalIgnoreCase))
            : null;

        var input = new TextBox
        {
            PlaceholderText = "{define: C base-fret 1 frets x 3 2 0 1 0}",
            Text = existing?.RawDirective ?? BuildSuggestedDefine(chordAtCaret)
        };

        var errorText = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = existing is null ? "Define chord diagram" : $"Edit {existing.Name} diagram",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Use a ChordPro define directive. This updates the same custom chord list used in Settings.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.8
                    },
                    input,
                    errorText
                }
            }
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            var raw = input.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                errorText.Text = "Please enter a {define: ...} directive.";
                errorText.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }

            bool success;
            string error;

            if (existing is not null)
            {
                success = CustomChordService.TryUpdate(existing, raw, out error);
            }
            else
            {
                success = CustomChordService.TryAdd(raw, out error);
            }

            if (!success)
            {
                errorText.Text = error;
                errorText.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            RefreshPreview();
        }
    }

    private string BuildSuggestedDefine(string? chordName)
    {
        if (string.IsNullOrWhiteSpace(chordName))
        {
            return "{define: ChordName base-fret 1 frets x x x x x x}";
        }

        return $"{{define: {chordName} base-fret 1 frets x x x x x x}}";
    }

    private string? TryGetChordAtCaret()
    {
        var text = EditorTextBox.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return null;
        }

        var caret = Math.Clamp(EditorTextBox.SelectionStart, 0, text.Length);
        var searchIndex = caret > 0 && text[caret - 1] == ']' ? caret - 1 : caret;

        var open = text.LastIndexOf('[', searchIndex);
        if (open < 0)
        {
            return null;
        }

        var close = text.IndexOf(']', open + 1);
        if (close < 0 || caret < open || caret > close + 1)
        {
            return null;
        }

        var chord = text.Substring(open + 1, close - open - 1).Trim();
        if (chord.StartsWith("*", StringComparison.Ordinal))
        {
            chord = chord[1..].Trim();
        }

        return string.IsNullOrWhiteSpace(chord) ? null : chord;
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

    private sealed class DatamuseWordResult
    {
        [JsonPropertyName("word")]
        public string? Word { get; set; }
    }
}
