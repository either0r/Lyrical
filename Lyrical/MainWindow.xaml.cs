using Lyrical.Models;
using Lyrical.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lyrical
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            RootFrame.Navigate(typeof(SongListPage));
            if (AppNavigationView.MenuItems[0] is NavigationViewItem item)
            {
                AppNavigationView.SelectedItem = item;
            }
        }

        private void AppNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
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
                    RootFrame.Navigate(typeof(SongEditorPage), SongDocument.CreateNew());
                    break;
            }
        }
    }
}
