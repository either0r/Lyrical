using Lyrical.Models;
using Lyrical.Pages;
using Lyrical.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Windowing;

namespace Lyrical
{
    public sealed partial class MainWindow : Window
    {
        public static MainWindow? Instance { get; private set; }

        private bool _isForceClosing;
        private bool _didRunActivationPrompts;

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();

            AppWindow.SetIcon("Assets/StoreLogo.scale-125.ico");

            if (Content is FrameworkElement root)
            {
                root.RequestedTheme = ThemeService.Current;
                root.Loaded += Root_Loaded;
            }

            ApplyTitleBarTheme(ThemeService.Current);
            ThemeService.ThemeChanged += OnThemeChanged;

            Closed += MainWindow_Closed;

            RootFrame.Navigate(typeof(SongListPage));
            if (AppNavigationView.MenuItems[0] is NavigationViewItem item)
            {
                AppNavigationView.SelectedItem = item;
            }
        }

        private async void Root_Loaded(object sender, RoutedEventArgs e)
        {
            if (_didRunActivationPrompts)
            {
                return;
            }

            if (sender is not FrameworkElement root || root.XamlRoot is null)
            {
                return;
            }

            _didRunActivationPrompts = true;
            root.Loaded -= Root_Loaded;

            var xamlRoot = root.XamlRoot;

            if (WhatsNewService.ShouldShow())
            {
                var whatsNewDialog = new ContentDialog
                {
                    XamlRoot = xamlRoot,
                    Title = $"What's new in {WhatsNewService.CurrentVersion}",
                    Content = "• New and improved editing flow\n• Settings cleanup and version visibility\n• Ongoing stability improvements",
                    CloseButtonText = "Got it"
                };

                await whatsNewDialog.ShowAsync();
                WhatsNewService.MarkSeen();
            }

            if (!DesktopShortcutService.HasPrompted && !DesktopShortcutService.ShortcutExists)
            {
                DesktopShortcutService.HasPrompted = true;

                var shortcutDialog = new ContentDialog
                {
                    XamlRoot = xamlRoot,
                    Title = "Add desktop shortcut?",
                    Content = "Would you like to add a Lyrical shortcut to your desktop?",
                    PrimaryButtonText = "Yes",
                    CloseButtonText = "No"
                };

                if (await shortcutDialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    DesktopShortcutService.CreateShortcut();
                }
            }
        }

        private void OnThemeChanged(ElementTheme theme)
        {
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

        private async void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (_isForceClosing)
            {
                ThemeService.ThemeChanged -= OnThemeChanged;
                if (Content is FrameworkElement root)
                {
                    root.Loaded -= Root_Loaded;
                }
                return;
            }

            var dirtyEditors = GetAllDirtyEditors();
            if (dirtyEditors.Count > 0)
            {
                args.Handled = true;

                var message = dirtyEditors.Count == 1
                    ? "You have unsaved changes. Do you want to save before closing?"
                    : $"You have unsaved changes in {dirtyEditors.Count} tabs. Do you want to save all before closing?";

                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Title = "Unsaved changes",
                    Content = message,
                    PrimaryButtonText = "Save all",
                    SecondaryButtonText = "Discard all",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    foreach (var editor in dirtyEditors)
                    {
                        await editor.TriggerSaveAsync();
                    }
                    _isForceClosing = true;
                    this.Close();
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    _isForceClosing = true;
                    this.Close();
                }
            }

            if (!args.Handled)
            {
                ThemeService.ThemeChanged -= OnThemeChanged;
                if (Content is FrameworkElement root)
                {
                    root.Loaded -= Root_Loaded;
                }
            }
        }

        private List<SongEditorPage> GetAllDirtyEditors()
        {
            var dirty = new List<SongEditorPage>();
            foreach (var tab in EditorTabView.TabItems.OfType<TabViewItem>())
            {
                if (tab.Content is Frame frame && frame.Content is SongEditorPage editor && editor.HasPendingChanges)
                {
                    dirty.Add(editor);
                }
            }
            return dirty;
        }

        private async void AppNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer?.Tag is not string tag)
            {
                return;
            }

            switch (tag)
            {
                case "songs":
                    ShowFrameContent();
                    if (RootFrame.CurrentSourcePageType != typeof(SongListPage))
                    {
                        RootFrame.Navigate(typeof(SongListPage));
                    }
                    else if (RootFrame.Content is SongListPage songListPage)
                    {
                        _ = songListPage.RefreshAsync();
                    }
                    break;
                case "new-song":
                    var title = await NewSongDialog.PromptAsync(Content.XamlRoot);
                    if (title is not null)
                    {
                        OpenSongTab(SongDocument.CreateNew(title));
                    }
                    break;
                case "settings":
                    ShowFrameContent();
                    if (RootFrame.CurrentSourcePageType != typeof(SettingsPage))
                    {
                        RootFrame.Navigate(typeof(SettingsPage));
                    }
                    break;
            }
        }

        private void ShowFrameContent()
        {
            RootFrame.Visibility = Visibility.Visible;
            EditorTabView.Visibility = Visibility.Collapsed;
        }

        private void ShowTabContent()
        {
            RootFrame.Visibility = Visibility.Collapsed;
            EditorTabView.Visibility = Visibility.Visible;
        }

        public void OpenSongTab(SongDocument song)
        {
            // Check if this song is already open in a tab (match by FileName + RelativeFolderPath)
            if (!string.IsNullOrWhiteSpace(song.FileName))
            {
                foreach (var existingTab in EditorTabView.TabItems.OfType<TabViewItem>())
                {
                    if (existingTab.Tag is SongDocument existingSong
                        && string.Equals(existingSong.RelativeFilePath, song.RelativeFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        EditorTabView.SelectedItem = existingTab;
                        ShowTabContent();
                        return;
                    }
                }
            }

            var frame = new Frame();
            frame.Navigate(typeof(SongEditorPage), song);

            var tab = new TabViewItem
            {
                Header = song.Title ?? "Untitled",
                Content = frame,
                Tag = song,
                IsClosable = true
            };

            song.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SongDocument.Title))
                {
                    UpdateTabHeader(tab);
                }
            };

            EditorTabView.TabItems.Add(tab);
            EditorTabView.SelectedItem = tab;
            ShowTabContent();
        }

        public void UpdateTabHeader(TabViewItem tab)
        {
            if (tab.Tag is SongDocument song)
            {
                var title = string.IsNullOrWhiteSpace(song.Title) ? "Untitled" : song.Title;
                if (tab.Content is Frame frame && frame.Content is SongEditorPage editor && editor.HasPendingChanges)
                {
                    title = "● " + title;
                }
                tab.Header = title;
            }
        }

        public void UpdateCurrentTabDirtyIndicator()
        {
            if (EditorTabView.SelectedItem is TabViewItem tab)
            {
                UpdateTabHeader(tab);
            }
        }

        private async void EditorTabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            if (args.Item is TabViewItem tab)
            {
                await CloseTabAsync(tab);
            }
        }

        public async System.Threading.Tasks.Task<bool> CloseTabAsync(TabViewItem tab)
        {
            if (tab.Content is Frame frame && frame.Content is SongEditorPage editor && editor.HasPendingChanges)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Title = "Unsaved changes",
                    Content = "You have unsaved changes. Save now or discard?",
                    PrimaryButtonText = "Save",
                    SecondaryButtonText = "Discard",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    await editor.TriggerSaveAsync();
                }
                else if (result != ContentDialogResult.Secondary)
                {
                    return false; // Cancel
                }
            }

            EditorTabView.TabItems.Remove(tab);

            if (EditorTabView.TabItems.Count == 0)
            {
                ShowFrameContent();
                if (RootFrame.CurrentSourcePageType != typeof(SongListPage))
                {
                    RootFrame.Navigate(typeof(SongListPage));
                }
                else if (RootFrame.Content is SongListPage songListPage)
                {
                    _ = songListPage.RefreshAsync();
                }
                if (AppNavigationView.MenuItems[0] is NavigationViewItem navItem)
                {
                    AppNavigationView.SelectedItem = navItem;
                }
            }

            return true;
        }

        public async System.Threading.Tasks.Task<bool> CloseCurrentTabAsync()
        {
            if (EditorTabView.SelectedItem is TabViewItem tab)
            {
                return await CloseTabAsync(tab);
            }
            return true;
        }

        private void EditorTabView_SelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            // No-op for now; tab switching is handled by TabView automatically
        }

        public async void OpenActivationFile()
        {
            var file = FileActivationService.ActivationFile;
            if (file == null)
                return;

            try
            {
                // Load the song from the activation file
                var song = await SongStorageService.LoadSongFromFileAsync(file);
                if (song != null)
                {
                    OpenSongTab(song);
                }
                FileActivationService.ClearActivationFile();
            }
            catch
            {
                // Error loading file - show message
                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Title = "Could not open file",
                    Content = "The selected file could not be opened.",
                    CloseButtonText = "OK"
                };
                _ = await dialog.ShowAsync();
                FileActivationService.ClearActivationFile();
            }
        }
    }
}
