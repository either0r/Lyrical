using Lyrical.Models;
using Lyrical.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;

namespace Lyrical.Pages;

public sealed partial class SettingsPage : Page
{
    public ObservableCollection<CustomChordDefinition> Chords { get; } = [];

    private CustomChordDefinition? _editingChord;
    private bool _themeSelectionReady;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadThemeSelection();
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
