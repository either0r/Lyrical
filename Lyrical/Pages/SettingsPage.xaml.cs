using Lyrical.Models;
using Lyrical.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.System;

namespace Lyrical.Pages;

public sealed partial class SettingsPage : Page
{
    public ObservableCollection<CustomChordDefinition> Chords { get; } = [];

    private CustomChordDefinition? _editingChord;
    private bool _themeSelectionReady;
    private bool _autoSaveSelectionReady;
    private bool _librarySelectionReady;
    private bool _exportSelectionReady;
    private bool _updateSelectionReady;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadThemeSelection();
        LoadAutoSaveSettings();
        await LoadLibrarySettingsAsync();
        LoadUpdateSettings();
        LoadShortcutSettings();
        RefreshList();
    }

    // ── Theme ─────────────────────────────────────────────────────────────────

    private void LoadThemeSelection()
    {
        _themeSelectionReady = false;
        var tag = ThemeService.Current.ToString();
        foreach (var item in ThemeRadioButtons.Items)
        {
            if (item is RadioButton rb && rb.Tag as string == tag)
            {
                ThemeRadioButtons.SelectedItem = rb;
                break;
            }
        }
        _themeSelectionReady = true;
    }

    private void ThemeRadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_themeSelectionReady)
        {
            return;
        }

        if (ThemeRadioButtons.SelectedItem is RadioButton rb
            && rb.Tag is string tag
            && System.Enum.TryParse<ElementTheme>(tag, out var theme))
        {
            ThemeService.Apply(theme);
        }
    }

    // ── Song library ───────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task LoadLibrarySettingsAsync()
    {
        _librarySelectionReady = false;
        _exportSelectionReady = false;

        LibraryModeComboBox.SelectedIndex = SongStorageService.ActiveLibraryMode == SongLibraryMode.Shared ? 1 : 0;
        LocalFolderText.Text = await SongStorageService.GetLibraryFolderDisplayNameAsync(SongLibraryMode.Local);
        SharedFolderText.Text = await SongStorageService.GetLibraryFolderDisplayNameAsync(SongLibraryMode.Shared);
        ExportHtmlOnSaveCheckBox.IsChecked = ExportSettingsService.ExportHtmlOnSave;

        _librarySelectionReady = true;
        _exportSelectionReady = true;
    }

    private void LibraryModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_librarySelectionReady)
        {
            return;
        }

        SongStorageService.ActiveLibraryMode = LibraryModeComboBox.SelectedIndex == 1
            ? SongLibraryMode.Shared
            : SongLibraryMode.Local;
    }

    private async void ChooseLocalFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (await SongStorageService.ConfigureLibraryFolderAsync(SongLibraryMode.Local))
        {
            LocalFolderText.Text = await SongStorageService.GetLibraryFolderDisplayNameAsync(SongLibraryMode.Local);
        }
    }

    private async void ChooseSharedFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (await SongStorageService.ConfigureLibraryFolderAsync(SongLibraryMode.Shared))
        {
            SharedFolderText.Text = await SongStorageService.GetLibraryFolderDisplayNameAsync(SongLibraryMode.Shared);
        }
    }

    private void ExportHtmlOnSaveCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_exportSelectionReady)
        {
            return;
        }

        ExportSettingsService.ExportHtmlOnSave = ExportHtmlOnSaveCheckBox.IsChecked == true;
    }

    // ── Updates ────────────────────────────────────────────────────────────────

    private void LoadUpdateSettings()
    {
        _updateSelectionReady = false;

        var version = Windows.ApplicationModel.Package.Current.Id.Version;
        CurrentVersionText.Text = $"Version: {version.Major}.{version.Minor}.{version.Build}";

        UpdateNotificationsCheckBox.IsChecked = UpdateSettingsService.NotificationsEnabled;
        UpdateCheckStatusText.Text = string.Empty;
        _updateSelectionReady = true;
    }
    
    private void UpdateNotificationsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_updateSelectionReady)
        {
            return;
        }

        UpdateSettingsService.NotificationsEnabled = UpdateNotificationsCheckBox.IsChecked == true;
    }

    private async void CheckForUpdatesNowButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateCheckStatusText.Text = "Checking...";

        var check = await AppUpdateService.CheckForUpdateAsync(force: true);
        if (!check.IsChecked)
        {
            UpdateCheckStatusText.Text = "No check performed.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(check.Error))
        {
            UpdateCheckStatusText.Text = "Unable to check for updates.";
            return;
        }

        if (!check.IsUpdateAvailable)
        {
            UpdateCheckStatusText.Text = "You are up to date.";
            return;
        }

        UpdateCheckStatusText.Text = $"Update {check.LatestVersion} available.";

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Update available",
            Content = $"A newer version ({check.LatestVersion}) is available. You are on {check.CurrentVersion}.",
            PrimaryButtonText = "Open download page",
            CloseButtonText = "Close"
        };

        var result = await dialog.ShowAsync();
        AppUpdateService.MarkVersionNotified(check.LatestVersion);

        if (result == ContentDialogResult.Primary)
        {
            _ = await Launcher.LaunchUriAsync(new System.Uri(check.ReleaseUrl));
        }
    }

    // ── Editor auto-save ──────────────────────────────────────────────────────

    private void LoadAutoSaveSettings()
    {
        _autoSaveSelectionReady = false;

        AutoSaveModeComboBox.SelectedIndex = EditorSettingsService.AutoSaveMode switch
        {
            AutoSaveMode.OnFocusChange => 1,
            AutoSaveMode.AfterDelay => 2,
            _ => 0
        };

        AutoSaveDelayNumberBox.Value = EditorSettingsService.AutoSaveDelaySeconds;
        UpdateAutoSaveDelayEnabledState();

        _autoSaveSelectionReady = true;
    }

    private void AutoSaveModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_autoSaveSelectionReady)
        {
            return;
        }

        EditorSettingsService.AutoSaveMode = AutoSaveModeComboBox.SelectedIndex switch
        {
            1 => AutoSaveMode.OnFocusChange,
            2 => AutoSaveMode.AfterDelay,
            _ => AutoSaveMode.Off
        };

        UpdateAutoSaveDelayEnabledState();
    }

    private void AutoSaveDelayNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_autoSaveSelectionReady)
        {
            return;
        }

        if (double.IsNaN(sender.Value))
        {
            sender.Value = EditorSettingsService.AutoSaveDelaySeconds;
            return;
        }

        var delay = (int)System.Math.Round(sender.Value);
        EditorSettingsService.AutoSaveDelaySeconds = delay;
        sender.Value = EditorSettingsService.AutoSaveDelaySeconds;
    }

    private void UpdateAutoSaveDelayEnabledState()
    {
        AutoSaveDelayNumberBox.IsEnabled = AutoSaveModeComboBox.SelectedIndex == 2;
    }

    // ── Desktop shortcut ───────────────────────────────────────────────────────

    private void LoadShortcutSettings()
    {
        var exists = DesktopShortcutService.ShortcutExists;
        ShortcutStatusText.Text = exists
            ? "A Lyrical shortcut is on your desktop."
            : "No desktop shortcut is currently set up.";
        CreateShortcutButton.IsEnabled = !exists;
        RemoveShortcutButton.IsEnabled = exists;
    }

    private void CreateShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (DesktopShortcutService.CreateShortcut())
        {
            LoadShortcutSettings();
        }
        else
        {
            ShortcutStatusText.Text = "Could not create the shortcut. Check app permissions.";
        }
    }

    private void RemoveShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (DesktopShortcutService.RemoveShortcut())
        {
            LoadShortcutSettings();
        }
        else
        {
            ShortcutStatusText.Text = "Could not remove the shortcut.";
        }
    }

    // ── Edit mode helpers ─────────────────────────────────────────────────────

    private void EnterEditMode(CustomChordDefinition def)
    {
        _editingChord = def;
        DefineInputBox.Text = def.RawDirective;
        AddSaveButton.Content = "Save";
        CancelEditButton.Visibility = Visibility.Visible;
        DefineInputBox.Focus(FocusState.Programmatic);
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ExitEditMode()
    {
        _editingChord = null;
        DefineInputBox.Text = string.Empty;
        AddSaveButton.Content = "Add";
        CancelEditButton.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void AddSaveButton_Click(object sender, RoutedEventArgs e)
    {
        TryAddFromInput();
    }

    private void CancelEditButton_Click(object sender, RoutedEventArgs e)
    {
        ExitEditMode();
    }

    private void DefineInputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            TryAddFromInput();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape && _editingChord is not null)
        {
            ExitEditMode();
            e.Handled = true;
        }
    }

    private void TryAddFromInput()
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var raw = DefineInputBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        if (_editingChord is not null)
        {
            if (!CustomChordService.TryUpdate(_editingChord, raw, out var updateError))
            {
                ErrorText.Text = updateError;
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            ExitEditMode();
        }
        else
        {
            if (!CustomChordService.TryAdd(raw, out var addError))
            {
                ErrorText.Text = addError;
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            DefineInputBox.Text = string.Empty;
        }

        RefreshList();
    }

    private void EditChordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: CustomChordDefinition def })
        {
            EnterEditMode(def);
        }
    }

    private void RemoveChordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: CustomChordDefinition def })
        {
            if (_editingChord == def)
            {
                ExitEditMode();
            }

            CustomChordService.Remove(def);
            RefreshList();
        }
    }

    // ── Chord list ────────────────────────────────────────────────────────────

    private void RefreshList()
    {
        Chords.Clear();
        foreach (var def in CustomChordService.Definitions)
        {
            Chords.Add(def);
        }
    }
}
