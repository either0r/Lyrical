using Lyrical.Models;
using Lyrical.Pages;
using Lyrical.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lyrical
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            if (Content is FrameworkElement root)
            {
                root.RequestedTheme = ThemeService.Current;
            }

            RootFrame.Navigate(typeof(SongListPage));
            if (AppNavigationView.MenuItems[0] is NavigationViewItem item)
            {
                AppNavigationView.SelectedItem = item;
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
                        RootFrame.Navigate(typeof(SongListPage));
                    }
                    break;
                case "new-song":
                    var title = await NewSongDialog.PromptAsync(Content.XamlRoot);
                    if (title is not null)
                    {
                        RootFrame.Navigate(typeof(SongEditorPage), SongDocument.CreateNew(title));
                    }
                    break;
                case "settings":
                    if (RootFrame.CurrentSourcePageType != typeof(SettingsPage))
                    {
                        RootFrame.Navigate(typeof(SettingsPage));
                    }
                    break;
            }
        }
    }
}
