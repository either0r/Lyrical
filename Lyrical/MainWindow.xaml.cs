using Lyrical.Models;
using Lyrical.Pages;
using Lyrical.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.System;

namespace Lyrical
{
    public sealed partial class MainWindow : Window
    {
        private bool _didRunInitialUpdateCheck;
        private bool _isForceClosing;

        public MainWindow()
        {
            InitializeComponent();

            if (Content is FrameworkElement root)
            {
                root.RequestedTheme = ThemeService.Current;
            }

            Activated += MainWindow_Activated;
            Closed += MainWindow_Closed;

            RootFrame.Navigate(typeof(SongListPage));
            if (AppNavigationView.MenuItems[0] is NavigationViewItem item)
            {
                AppNavigationView.SelectedItem = item;
            }
        }

        private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (_didRunInitialUpdateCheck)
            {
                return;
            }

            _didRunInitialUpdateCheck = true;

            var check = await AppUpdateService.CheckForUpdateAsync(force: false);
            if (check.IsChecked && check.IsUpdateAvailable)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Title = "Update available",
                    Content = $"A newer version ({check.LatestVersion}) is available. You are on {check.CurrentVersion}.",
                    PrimaryButtonText = "Open download page",
                    CloseButtonText = "Later"
                };

                var result = await dialog.ShowAsync();
                AppUpdateService.MarkVersionNotified(check.LatestVersion);

                if (result == ContentDialogResult.Primary)
                {
                    _ = await Launcher.LaunchUriAsync(new System.Uri(check.ReleaseUrl));
                }
            }

            if (!DesktopShortcutService.HasPrompted && !DesktopShortcutService.ShortcutExists)
            {
                DesktopShortcutService.HasPrompted = true;

                var shortcutDialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
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

        private async void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (_isForceClosing)
                return;

            if (RootFrame.Content is SongEditorPage editor && editor.HasPendingChanges)
            {
                args.Handled = true;

                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Title = "Unsaved changes",
                    Content = "You have unsaved changes. Do you want to save before closing?",
                    PrimaryButtonText = "Save",
                    SecondaryButtonText = "Discard",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    await editor.TriggerSaveAsync();
                    _isForceClosing = true;
                    this.Close();
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    _isForceClosing = true;
                    this.Close();
                }
            }
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
                    if (RootFrame.CurrentSourcePageType != typeof(SongListPage))
                    {
                        if (await CheckUnsavedChangesAsync())
                        {
                            RootFrame.Navigate(typeof(SongListPage));
                        }
                    }
                    break;
                case "new-song":
                    if (await CheckUnsavedChangesAsync())
                    {
                        var title = await NewSongDialog.PromptAsync(Content.XamlRoot);
                        if (title is not null)
                        {
                            RootFrame.Navigate(typeof(SongEditorPage), SongDocument.CreateNew(title));
                        }
                    }
                    break;
                case "settings":
                    if (RootFrame.CurrentSourcePageType != typeof(SettingsPage))
                    {
                        if (await CheckUnsavedChangesAsync())
                        {
                            RootFrame.Navigate(typeof(SettingsPage));
                        }
                    }
                    break;
            }
        }

        private async System.Threading.Tasks.Task<bool> CheckUnsavedChangesAsync()
        {
            if (RootFrame.Content is SongEditorPage editor && editor.HasPendingChanges)
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
                    editor.TriggerSave();
                    return true;
                }

                return result == ContentDialogResult.Secondary;
            }

            return true;
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
                    // Navigate to editor with the song
                    RootFrame.Navigate(typeof(SongEditorPage), song);
                    if (AppNavigationView.MenuItems.Count > 1 && AppNavigationView.MenuItems[1] is NavigationViewItem item)
                    {
                        AppNavigationView.SelectedItem = item;
                    }
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
