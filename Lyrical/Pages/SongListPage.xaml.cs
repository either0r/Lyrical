using Lyrical.Models;
using Lyrical.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Windows.ApplicationModel.DataTransfer;

namespace Lyrical.Pages;

public sealed partial class SongListPage : Page
{
    private const string DragSongPathKey = "LyricalSongRelativePath";

    private sealed record FolderChoice(string RelativePath, string DisplayName);

    public sealed class BreadcrumbItem
    {
        public string DisplayName { get; set; } = string.Empty;

        public string RelativePath { get; set; } = string.Empty;

        public string Separator { get; set; } = string.Empty;

        public BreadcrumbItem()
        {
        }

        public BreadcrumbItem(string displayName, string relativePath, string separator)
        {
            DisplayName = displayName;
            RelativePath = relativePath;
            Separator = separator;
        }
    }

    private readonly Dictionary<TreeViewNode, SongFolder> _folderNodeLookup = [];
    private string? _dragSongRelativePath;

    private SongFolder _folderTreeRoot = new()
    {
        Name = "All Songs",
        RelativePath = string.Empty,
        TotalSongCount = 0
    };

    private SongFolder _selectedFolder = new()
    {
        Name = "All Songs",
        RelativePath = string.Empty,
        TotalSongCount = 0
    };

    public ObservableCollection<SongDocument> Songs { get; } = [];

    public ObservableCollection<SongDocument> FilteredSongs { get; } = [];

    public ObservableCollection<BreadcrumbItem> Breadcrumbs { get; } = [];

    private readonly SemaphoreSlim _reloadLibraryGate = new(1, 1);

    public SongListPage()
    {
        InitializeComponent();
        SongScopeRadioButtons.SelectedIndex = 1;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ReloadLibraryAsync(_selectedFolder.RelativePath);
    }

    public async System.Threading.Tasks.Task RefreshAsync()
    {
        await ReloadLibraryAsync(_selectedFolder.RelativePath);
    }

    private async System.Threading.Tasks.Task ReloadLibraryAsync(string? selectedFolderPath = null, string? selectedSongPath = null)
    {
        await _reloadLibraryGate.WaitAsync();
        try
        {
            Songs.Clear();
            var loadedSongs = await SongStorageService.LoadSongsAsync();
            foreach (var song in loadedSongs)
            {
                Songs.Add(song);
            }

            _folderTreeRoot = await SongStorageService.LoadFolderTreeAsync();
            PopulateFolderTree(selectedFolderPath);
            ApplySongFilter(selectedSongPath);
        }
        finally
        {
            _reloadLibraryGate.Release();
        }
    }

    private void PopulateFolderTree(string? selectedFolderPath)
    {
        _folderNodeLookup.Clear();
        FolderTreeView.RootNodes.Clear();

        var rootNode = CreateTreeNode(_folderTreeRoot);
        rootNode.IsExpanded = true;
        FolderTreeView.RootNodes.Add(rootNode);

        var normalizedSelection = NormalizeRelativeFolderPath(selectedFolderPath);
        var selectedNode = FindNodeByRelativePath(rootNode, normalizedSelection) ?? rootNode;
        FolderTreeView.SelectedNode = selectedNode;
        SetSelectedFolder(selectedNode, selectedSongPath: null);
    }

    private TreeViewNode CreateTreeNode(SongFolder folder)
    {
        var node = new TreeViewNode
        {
            Content = folder.DisplayName,
            IsExpanded = folder.IsRoot
        };
        _folderNodeLookup[node] = folder;

        foreach (var child in folder.Children)
        {
            node.Children.Add(CreateTreeNode(child));
        }

        return node;
    }

    private TreeViewNode? FindNodeByRelativePath(TreeViewNode node, string relativePath)
    {
        if (_folderNodeLookup.TryGetValue(node, out var folder)
            && string.Equals(folder.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var match = FindNodeByRelativePath(child, relativePath);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private void FolderTreeView_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        SetSelectedFolder(sender.SelectedNode);
    }

    private void SongScopeRadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplySongFilter();
    }

    private void BreadcrumbButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: BreadcrumbItem breadcrumb })
        {
            return;
        }

        SelectFolderByPath(breadcrumb.RelativePath);
    }

    private void SongListView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.FirstOrDefault() is not SongDocument song)
        {
            e.Cancel = true;
            return;
        }

        _dragSongRelativePath = song.RelativeFilePath;
        e.Data.Properties[DragSongPathKey] = song.RelativeFilePath;
        e.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void SongListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        _dragSongRelativePath = null;
    }

    private void FolderTreeView_DragOver(object sender, DragEventArgs e)
    {
        var targetFolder = TryResolveDropTargetFolder(e.OriginalSource as DependencyObject);
        var dragSong = TryResolveDraggedSong(e.DataView);

        if (targetFolder is null || dragSong is null || IsDropTargetSameFolder(dragSong, targetFolder))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.Caption = $"Move to {(targetFolder.IsRoot ? "Library Root" : targetFolder.RelativePath)}";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
    }

    private async void FolderTreeView_Drop(object sender, DragEventArgs e)
    {
        var targetFolder = TryResolveDropTargetFolder(e.OriginalSource as DependencyObject) ?? _selectedFolder;
        var dragSong = TryResolveDraggedSong(e.DataView);
        if (targetFolder is null || dragSong is null || IsDropTargetSameFolder(dragSong, targetFolder))
        {
            return;
        }

        if (await SongStorageService.MoveSongAsync(dragSong, targetFolder.RelativePath))
        {
            await ReloadLibraryAsync(targetFolder.RelativePath, dragSong.RelativeFilePath);
        }
    }

    private SongFolder? TryResolveDropTargetFolder(DependencyObject? originalSource)
    {
        var treeViewItem = FindAncestor<TreeViewItem>(originalSource);
        if (treeViewItem?.DataContext is TreeViewNode node
            && _folderNodeLookup.TryGetValue(node, out var folderFromNode))
        {
            return folderFromNode;
        }

        if (FolderTreeView.SelectedNode is not null && _folderNodeLookup.TryGetValue(FolderTreeView.SelectedNode, out var selectedFolder))
        {
            return selectedFolder;
        }

        return _folderTreeRoot;
    }

    private SongDocument? TryResolveDraggedSong(DataPackageView? dataView)
    {
        if (dataView is not null
            && dataView.Properties.TryGetValue(DragSongPathKey, out var dragSongPath)
            && dragSongPath is string pathFromData)
        {
            return FindSongByRelativePath(pathFromData);
        }

        if (!string.IsNullOrWhiteSpace(_dragSongRelativePath))
        {
            return FindSongByRelativePath(_dragSongRelativePath);
        }

        return null;
    }

    private SongDocument? FindSongByRelativePath(string? relativeFilePath)
    {
        if (string.IsNullOrWhiteSpace(relativeFilePath))
        {
            return null;
        }

        return Songs.FirstOrDefault(song => string.Equals(song.RelativeFilePath, relativeFilePath, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDropTargetSameFolder(SongDocument song, SongFolder targetFolder)
    {
        return string.Equals(song.RelativeFolderPath, targetFolder.RelativePath, StringComparison.OrdinalIgnoreCase);
    }

    private static T? FindAncestor<T>(DependencyObject? dependencyObject) where T : DependencyObject
    {
        while (dependencyObject is not null)
        {
            if (dependencyObject is T match)
            {
                return match;
            }

            dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
        }

        return null;
    }

    private void SelectFolderByPath(string? relativePath, string? selectedSongPath = null)
    {
        if (FolderTreeView.RootNodes.Count == 0)
        {
            return;
        }

        var rootNode = FolderTreeView.RootNodes[0];
        var targetPath = NormalizeRelativeFolderPath(relativePath);
        var selectedNode = FindNodeByRelativePath(rootNode, targetPath) ?? rootNode;
        FolderTreeView.SelectedNode = selectedNode;
        SetSelectedFolder(selectedNode, selectedSongPath);
    }

    private void SetSelectedFolder(TreeViewNode? node, string? selectedSongPath = null)
    {
        _selectedFolder = node is not null && _folderNodeLookup.TryGetValue(node, out var folder)
            ? folder
            : _folderTreeRoot;

        RenameFolderButton.IsEnabled = !_selectedFolder.IsRoot;
        DeleteFolderButton.IsEnabled = !_selectedFolder.IsRoot;
        NewSongButton.Content = _selectedFolder.IsRoot
            ? "New Song"
            : "New Song Here";
        SongScopeRadioButtons.IsEnabled = !_selectedFolder.IsRoot;

        if (_selectedFolder.IsRoot)
        {
            SongScopeRadioButtons.SelectedIndex = 1;
        }

        ApplySongFilter(selectedSongPath);
    }

    private void ApplySongFilter(string? selectedSongPath = null)
    {
        FilteredSongs.Clear();

        IEnumerable<SongDocument> songs = Songs;
        if (!_selectedFolder.IsRoot)
        {
            songs = songs.Where(IsSongVisibleInSelection);
        }

        foreach (var song in songs.OrderBy(song => song.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            FilteredSongs.Add(song);
        }

        UpdateSongSummaryText();
        RestoreSongSelection(selectedSongPath);
    }

    private void RestoreSongSelection(string? selectedSongPath)
    {
        if (string.IsNullOrWhiteSpace(selectedSongPath))
        {
            SongListView.SelectedItem = null;
            return;
        }

        var match = FilteredSongs.FirstOrDefault(song => string.Equals(song.RelativeFilePath, selectedSongPath, StringComparison.OrdinalIgnoreCase));
        SongListView.SelectedItem = match;
        if (match is not null)
        {
            SongListView.ScrollIntoView(match);
        }
    }

    private bool IsSongVisibleInSelection(SongDocument song)
    {
        if (_selectedFolder.IsRoot)
        {
            return true;
        }

        var songPath = NormalizeRelativeFolderPath(song.RelativeFolderPath);
        var selectedPath = _selectedFolder.RelativePath;
        if (string.Equals(songPath, selectedPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IsIncludingSubfolders())
        {
            return false;
        }

        return songPath.StartsWith(selectedPath + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSongSummaryText()
    {
        SelectedFolderText.Text = _selectedFolder.IsRoot
            ? "Library Root"
            : _selectedFolder.Name;
        UpdateBreadcrumbs(_selectedFolder.RelativePath);

        var filterScope = _selectedFolder.IsRoot
            ? "entire library"
            : IsIncludingSubfolders()
                ? "folder and subfolders"
                : "selected folder only";

        SongCountText.Text = FilteredSongs.Count == 0
            ? $"No songs to show in the {filterScope}."
            : $"Showing {FilteredSongs.Count} song{(FilteredSongs.Count == 1 ? string.Empty : "s")} from the {filterScope}.";

        EmptyStateText.Text = FilteredSongs.Count == 0
            ? _selectedFolder.IsRoot
                ? "No songs found in the current library. Create a new song or choose a different library folder in Settings."
                : $"Nothing is in '{_selectedFolder.RelativePath}' yet. Create a song here or add a subfolder for a new song book."
            : string.Empty;

        FolderHintInfoBar.IsOpen = !_selectedFolder.IsRoot;
        FolderHintInfoBar.Message = IsIncludingSubfolders()
            ? $"New songs will save directly into '{_selectedFolder.RelativePath}'. Songs from child folders are also shown below. You can also drag songs onto folders in the tree."
            : $"New songs will save directly into '{_selectedFolder.RelativePath}'. You can also drag songs onto folders in the tree.";
    }

    private void UpdateBreadcrumbs(string? relativePath)
    {
        Breadcrumbs.Clear();
        Breadcrumbs.Add(new BreadcrumbItem("Library Root", string.Empty, string.Empty));

        var normalizedPath = NormalizeRelativeFolderPath(relativePath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return;
        }

        var currentPath = string.Empty;
        foreach (var segment in normalizedPath.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = CombineRelativePath(currentPath, segment);
            Breadcrumbs.Add(new BreadcrumbItem(segment, currentPath, "›"));
        }
    }

    private void SongListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SongDocument song)
        {
            MainWindow.Instance?.OpenSongTab(song);
        }
    }

    private async void NewSongButton_Click(object sender, RoutedEventArgs e)
    {
        var title = await NewSongDialog.PromptAsync(XamlRoot);
        if (title is null)
        {
            return;
        }

        MainWindow.Instance?.OpenSongTab(SongDocument.CreateNew(title, _selectedFolder.RelativePath));
    }

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".cho");
        picker.FileTypeFilter.Add("*");

        var window = App.MainAppWindow;
        if (window != null)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file == null)
        {
            return;
        }

        var song = await SongStorageService.LoadSongFromFileAsync(file);
        if (song != null)
        {
            MainWindow.Instance?.OpenSongTab(song);
        }
        else
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Could not open file",
                Content = "The selected file could not be opened.",
                CloseButtonText = "OK"
            };
            _ = await dialog.ShowAsync();
        }
    }

    private async void NewFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folderName = await PromptForTextAsync("New folder", "Folder name");
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        var targetPath = CombineRelativePath(_selectedFolder.IsRoot ? string.Empty : _selectedFolder.RelativePath, folderName);
        if (await SongStorageService.CreateFolderAsync(targetPath))
        {
            await ReloadLibraryAsync(targetPath);
        }
    }

    private async void RenameFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFolder.IsRoot)
        {
            return;
        }

        var newName = await PromptForTextAsync("Rename folder", "Folder name", _selectedFolder.Name);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        var parentPath = GetParentPath(_selectedFolder.RelativePath);
        if (await SongStorageService.RenameFolderAsync(_selectedFolder.RelativePath, newName))
        {
            await ReloadLibraryAsync(CombineRelativePath(parentPath, newName));
        }
    }

    private async void DeleteFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFolder.IsRoot)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete folder?",
            Content = $"Delete '{_selectedFolder.RelativePath}' and all songs inside it?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var parentPath = GetParentPath(_selectedFolder.RelativePath);
        if (await SongStorageService.DeleteFolderAsync(_selectedFolder.RelativePath))
        {
            await ReloadLibraryAsync(parentPath);
        }
    }

    private async void RenameSongButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: SongDocument song })
        {
            return;
        }

        var newTitle = await PromptForTextAsync("Rename song", "Song title", song.Title);
        if (string.IsNullOrWhiteSpace(newTitle) || string.Equals(newTitle, song.Title, StringComparison.CurrentCulture))
        {
            return;
        }

        if (await SongStorageService.RenameSongAsync(song, newTitle))
        {
            await ReloadLibraryAsync(_selectedFolder.RelativePath, song.RelativeFilePath);
        }
    }

    private async void MoveSongButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: SongDocument song })
        {
            return;
        }

        var targetFolderPath = await PromptForFolderSelectionAsync(song.RelativeFolderPath);
        if (targetFolderPath is null)
        {
            return;
        }

        if (await SongStorageService.MoveSongAsync(song, targetFolderPath))
        {
            await ReloadLibraryAsync(targetFolderPath, song.RelativeFilePath);
        }
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

        if (await SongStorageService.DeleteSongAsync(song))
        {
            await ReloadLibraryAsync(_selectedFolder.RelativePath);
        }
    }

    private async System.Threading.Tasks.Task<string?> PromptForTextAsync(string title, string placeholder, string initialValue = "")
    {
        var textBox = new TextBox
        {
            PlaceholderText = placeholder,
            Text = initialValue,
            MinWidth = 280
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = textBox,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? textBox.Text?.Trim()
            : null;
    }

    private async System.Threading.Tasks.Task<string?> PromptForFolderSelectionAsync(string currentFolderPath)
    {
        var options = BuildFolderChoices(_folderTreeRoot).ToList();
        var currentPath = NormalizeRelativeFolderPath(currentFolderPath);
        var comboBox = new ComboBox
        {
            ItemsSource = options,
            DisplayMemberPath = nameof(FolderChoice.DisplayName),
            SelectedValuePath = nameof(FolderChoice.RelativePath),
            MinWidth = 320,
            SelectedValue = currentPath
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Move song",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Current folder: {(string.IsNullOrWhiteSpace(currentPath) ? "Library Root" : currentPath)}",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.7
                    },
                    comboBox
                }
            },
            PrimaryButtonText = "Move",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? comboBox.SelectedValue as string ?? string.Empty
            : null;
    }

    private IEnumerable<FolderChoice> BuildFolderChoices(SongFolder folder)
    {
        yield return new FolderChoice(folder.RelativePath, folder.IsRoot ? "Library Root" : folder.RelativePath);

        foreach (var child in folder.Children)
        {
            foreach (var option in BuildFolderChoices(child))
            {
                yield return option;
            }
        }
    }

    private bool IsIncludingSubfolders()
    {
        return _selectedFolder.IsRoot || SongScopeRadioButtons.SelectedIndex == 1;
    }

    private static string NormalizeRelativeFolderPath(string? relativeFolderPath)
    {
        if (string.IsNullOrWhiteSpace(relativeFolderPath))
        {
            return string.Empty;
        }

        return relativeFolderPath
            .Trim()
            .Replace('/','\\')
            .Trim('\\');
    }

    private static string CombineRelativePath(string? left, string? right)
    {
        var normalizedLeft = NormalizeRelativeFolderPath(left);
        var normalizedRight = NormalizeRelativeFolderPath(right);

        if (string.IsNullOrWhiteSpace(normalizedLeft))
        {
            return normalizedRight;
        }

        if (string.IsNullOrWhiteSpace(normalizedRight))
        {
            return normalizedLeft;
        }

        return $"{normalizedLeft}\\{normalizedRight}";
    }

    private static string GetParentPath(string relativePath)
    {
        var normalizedPath = NormalizeRelativeFolderPath(relativePath);
        return NormalizeRelativeFolderPath(System.IO.Path.GetDirectoryName(normalizedPath));
    }
}
