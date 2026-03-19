using Lyrical.Models;
using Lyrical.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Lyrical.Pages;

public sealed partial class SongListPage : Page
{
    public ObservableCollection<SongDocument> Songs { get; } = [];

    public SongListPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        Songs.Clear();
        var loaded = await SongStorageService.LoadSongsAsync();
        foreach (var song in loaded)
        {
            Songs.Add(song);
        }
    }

    private void SongListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SongDocument song)
        {
            Frame.Navigate(typeof(SongEditorPage), song);
        }
    }

    private async void NewSongButton_Click(object sender, RoutedEventArgs e)
    {
        var title = await NewSongDialog.PromptAsync(XamlRoot);
        if (title is null)
        {
            return;
        }

        Frame.Navigate(typeof(SongEditorPage), SongDocument.CreateNew(title));
    }

    private async void DeleteSongButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: SongDocument song })
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete song?",
            Content = $"Are you sure you want to delete '{song.Title}'?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var deleted = await SongStorageService.DeleteSongAsync(song);
        if (deleted)
        {
            Songs.Remove(song);
        }
    }
}
