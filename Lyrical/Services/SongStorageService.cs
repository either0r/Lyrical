using Lyrical.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Lyrical.Services;

public sealed record SongSaveConflictCheckResult(
    bool HasConflict,
    bool IsMissing,
    DateTimeOffset? CurrentFileLastModified,
    string CurrentFileContent)
{
    public static SongSaveConflictCheckResult NoConflict() => new(false, false, null, string.Empty);
}

public static class SongStorageService
{
    private const string LocalFolderTokenSettingKey = "SongLibraryFolderTokenLocal";
    private const string SharedFolderTokenSettingKey = "SongLibraryFolderTokenShared";
    private const string ActiveLibraryModeSettingKey = "SongLibraryMode";
    private const string ExampleSongTitle = "Example Song - ChordPro Guide";
    private const string ExampleSeededLocalSettingKey = "ExampleSongSeededLocal";
    private const string ExampleSeededSharedSettingKey = "ExampleSongSeededShared";
    private const string CreatorDirective = "x_creator";

    public static SongLibraryMode ActiveLibraryMode
    {
        get
        {
            if (ApplicationData.Current.LocalSettings.Values[ActiveLibraryModeSettingKey] is string saved
                && Enum.TryParse<SongLibraryMode>(saved, out var parsed))
            {
                return parsed;
            }

            return SongLibraryMode.Local;
        }
        set
        {
            ApplicationData.Current.LocalSettings.Values[ActiveLibraryModeSettingKey] = value.ToString();
        }
    }

    public static async Task<IReadOnlyList<SongDocument>> LoadSongsAsync()
    {
        var folder = await TryGetStoredFolderAsync(ActiveLibraryMode);
        if (folder is null)
        {
            return [];
        }

        _ = await EnsureExampleSongSeededOnceAsync(folder, ActiveLibraryMode);

        var songs = new List<SongDocument>();
        await LoadSongsRecursiveAsync(folder, string.Empty, songs);

        return songs
            .OrderBy(song => song.RelativeFolderPath, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(song => song.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static async Task<SongFolder> LoadFolderTreeAsync()
    {
        var root = new SongFolder
        {
            Name = "All Songs",
            RelativePath = string.Empty
        };

        var folder = await TryGetStoredFolderAsync(ActiveLibraryMode);
        if (folder is null)
        {
            return root;
        }

        _ = await EnsureExampleSongSeededOnceAsync(folder, ActiveLibraryMode);
        await PopulateFolderTreeAsync(folder, root);
        return root;
    }

    public static async Task<SongDocument?> LoadSongFromFileAsync(IStorageFile file)
    {
        try
        {
            var content = await FileIO.ReadTextAsync(file);
            var song = CreateSongFromChordPro(content);
            song.FileName = file.Name;
            song.RelativeFolderPath = string.Empty;
            var properties = await file.GetBasicPropertiesAsync();
            song.LastModified = properties.DateModified;
            return song;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<SongSaveConflictCheckResult> CheckForExternalChangeAsync(SongDocument song)
    {
        if (string.IsNullOrWhiteSpace(song.FileName))
        {
            return SongSaveConflictCheckResult.NoConflict();
        }

        var folder = await TryGetStoredFolderAsync(ActiveLibraryMode);
        if (folder is null)
        {
            return SongSaveConflictCheckResult.NoConflict();
        }

        var file = await TryGetSongFileAsync(folder, song);
        if (file is null)
        {
            return new SongSaveConflictCheckResult(
                HasConflict: true,
                IsMissing: true,
                CurrentFileLastModified: null,
                CurrentFileContent: string.Empty);
        }

        var properties = await file.GetBasicPropertiesAsync();
        var currentModified = properties.DateModified;

        var hasConflict = currentModified > song.LastModified.AddSeconds(1);
        if (!hasConflict)
        {
            return SongSaveConflictCheckResult.NoConflict();
        }

        var currentContent = await FileIO.ReadTextAsync(file);
        return new SongSaveConflictCheckResult(
            HasConflict: true,
            IsMissing: false,
            CurrentFileLastModified: currentModified,
            CurrentFileContent: currentContent);
    }

    public static async Task<bool> SaveSongAsync(SongDocument song)
    {
        var folder = await GetOrPromptForSongFolderAsync(ActiveLibraryMode);
        if (folder is null)
        {
            return false;
        }

        return await SaveSongToFolderAsync(song, folder, saveAsCopy: false);
    }

    public static async Task<bool> SaveSongAsCopyAsync(SongDocument song)
    {
        var folder = await GetOrPromptForSongFolderAsync(ActiveLibraryMode);
        if (folder is null)
        {
            return false;
        }

        return await SaveSongToFolderAsync(song, folder, saveAsCopy: true);
    }

    public static async Task<bool> SaveSongSilentlyAsync(SongDocument song)
    {
        var folder = await TryGetStoredFolderAsync(ActiveLibraryMode);
        if (folder is null)
        {
            return false;
        }

        return await SaveSongToFolderAsync(song, folder, saveAsCopy: false);
    }

    public static async Task<bool> DeleteSongAsync(SongDocument song)
    {
        if (string.IsNullOrWhiteSpace(song.FileName))
        {
            return false;
        }

        var rootFolder = await TryGetStoredFolderAsync(ActiveLibraryMode);
        if (rootFolder is null)
        {
            return false;
        }

        var file = await TryGetSongFileAsync(rootFolder, song);
        if (file is null)
        {
            return false;
        }

        await file.DeleteAsync(StorageDeleteOption.Default);

        var htmlFile = await TryGetStorageFileAsync(rootFolder, Path.ChangeExtension(song.RelativeFilePath, ".html"));
        if (htmlFile is not null)
        {
            await htmlFile.DeleteAsync(StorageDeleteOption.Default);
        }

        return true;
    }

    public static async Task<bool> RenameSongAsync(SongDocument song, string newTitle)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(newTitle)
            ? string.Empty
            : newTitle.Trim();
        if (normalizedTitle.Length == 0)
        {
            return false;
        }

        song.ChordPro = UpsertDirective(song.ChordPro, "title", normalizedTitle);
        song.Title = normalizedTitle;

        var rootFolder = await GetOrPromptForSongFolderAsync(ActiveLibraryMode);
        if (rootFolder is null)
        {
            return false;
        }

        return await SaveSongToFolderAsync(song, rootFolder, saveAsCopy: false);
    }

    public static async Task<bool> CreateFolderAsync(string relativePath)
    {
        var normalized = NormalizeRelativeFolderPath(relativePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var rootFolder = await GetOrPromptForSongFolderAsync(ActiveLibraryMode);
        if (rootFolder is null)
        {
            return false;
        }

        var created = await GetFolderByRelativePathAsync(rootFolder, normalized, createIfMissing: true);
        return created is not null;
    }

    public static async Task<bool> RenameFolderAsync(string relativePath, string newName)
    {
        var normalizedPath = NormalizeRelativeFolderPath(relativePath);
        var sanitizedName = BuildFolderName(newName);
        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(sanitizedName))
        {
            return false;
        }

        var rootFolder = await GetOrPromptForSongFolderAsync(ActiveLibraryMode);
        if (rootFolder is null)
        {
            return false;
        }

        var folder = await GetFolderByRelativePathAsync(rootFolder, normalizedPath);
        if (folder is null)
        {
            return false;
        }

        await folder.RenameAsync(sanitizedName, NameCollisionOption.FailIfExists);
        return true;
    }

    public static async Task<bool> DeleteFolderAsync(string relativePath)
    {
        var normalized = NormalizeRelativeFolderPath(relativePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var rootFolder = await GetOrPromptForSongFolderAsync(ActiveLibraryMode);
        if (rootFolder is null)
        {
            return false;
        }

        var folder = await GetFolderByRelativePathAsync(rootFolder, normalized);
        if (folder is null)
        {
            return false;
        }

        await folder.DeleteAsync(StorageDeleteOption.Default);
        return true;
    }

    public static async Task<bool> MoveSongAsync(SongDocument song, string targetFolderPath)
    {
        var normalizedTargetFolderPath = NormalizeRelativeFolderPath(targetFolderPath);
        var sourceFolderPath = song.RelativeFolderPath;

        if (string.Equals(sourceFolderPath, normalizedTargetFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(song.FileName))
        {
            song.RelativeFolderPath = normalizedTargetFolderPath;
            return true;
        }

        var rootFolder = await GetOrPromptForSongFolderAsync(ActiveLibraryMode);
        if (rootFolder is null)
        {
            return false;
        }

        var sourceRelativeFilePath = CombineRelativePath(sourceFolderPath, song.FileName);
        var sourceFile = await TryGetStorageFileAsync(rootFolder, sourceRelativeFilePath);
        if (sourceFile is null)
        {
            return false;
        }

        var destinationFolder = await GetFolderByRelativePathAsync(rootFolder, normalizedTargetFolderPath, createIfMissing: true);
        if (destinationFolder is null)
        {
            return false;
        }

        var movedFile = await sourceFile.CopyAsync(destinationFolder, song.FileName, NameCollisionOption.GenerateUniqueName);
        await sourceFile.DeleteAsync(StorageDeleteOption.Default);

        var sourceHtmlRelativePath = Path.ChangeExtension(sourceRelativeFilePath, ".html");
        var sourceHtmlFile = await TryGetStorageFileAsync(rootFolder, sourceHtmlRelativePath);
        if (sourceHtmlFile is not null)
        {
            var targetHtmlName = Path.ChangeExtension(movedFile.Name, ".html");
            var movedHtmlFile = await sourceHtmlFile.CopyAsync(destinationFolder, targetHtmlName, NameCollisionOption.ReplaceExisting);
            await sourceHtmlFile.DeleteAsync(StorageDeleteOption.Default);
            _ = movedHtmlFile;
        }

        song.RelativeFolderPath = normalizedTargetFolderPath;
        song.FileName = movedFile.Name;
        var properties = await movedFile.GetBasicPropertiesAsync();
        song.LastModified = properties.DateModified;
        return true;
    }

    public static async Task<bool> ConfigureLibraryFolderAsync(SongLibraryMode mode)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        if (App.MainAppWindow is null)
        {
            return false;
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainAppWindow));
        var selected = await picker.PickSingleFolderAsync();
        if (selected is null)
        {
            return false;
        }

        var token = StorageApplicationPermissions.FutureAccessList.Add(selected);
        ApplicationData.Current.LocalSettings.Values[GetFolderTokenKey(mode)] = token;
        return true;
    }

    public static async Task<string> GetLibraryFolderDisplayNameAsync(SongLibraryMode mode)
    {
        var folder = await TryGetStoredFolderAsync(mode);
        if (folder is null)
        {
            return "Not configured";
        }

        var modeLabel = mode == SongLibraryMode.Shared ? "Shared" : "Local";
        return $"{modeLabel}: {folder.Name}";
    }

    private static async Task<bool> SaveSongToFolderAsync(SongDocument song, StorageFolder rootFolder, bool saveAsCopy)
    {
        EnsureCreatorDirective(song);
        ApplyMetadataFromChordPro(song);
        song.RelativeFolderPath = NormalizeRelativeFolderPath(song.RelativeFolderPath);

        var targetSongFolder = await GetFolderByRelativePathAsync(rootFolder, song.RelativeFolderPath, createIfMissing: true);
        if (targetSongFolder is null)
        {
            return false;
        }

        var previousRelativeFilePath = song.RelativeFilePath;
        var targetFileName = BuildFileName(song.Title);

        if (saveAsCopy)
        {
            targetFileName = await BuildUniqueCopyFileNameAsync(targetSongFolder, targetFileName);
        }

        var file = await targetSongFolder.CreateFileAsync(targetFileName, CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(file, song.ChordPro);
        song.FileName = file.Name;

        if (ExportSettingsService.ExportHtmlOnSave)
        {
            await ExportHtmlCompanionAsync(song, targetSongFolder);
        }

        // Create timestamped backup after successful save
        if (!saveAsCopy)
        {
            await BackupService.CreateBackupAsync(song.RelativeFilePath, song.ChordPro);
        }

        if (!saveAsCopy
            && !string.IsNullOrWhiteSpace(previousRelativeFilePath)
            && !string.Equals(previousRelativeFilePath, song.RelativeFilePath, StringComparison.OrdinalIgnoreCase))
        {
            var previousFile = await TryGetStorageFileAsync(rootFolder, previousRelativeFilePath);
            if (previousFile is not null)
            {
                await previousFile.DeleteAsync(StorageDeleteOption.Default);
            }

            var previousHtmlFile = await TryGetStorageFileAsync(rootFolder, Path.ChangeExtension(previousRelativeFilePath, ".html"));
            if (previousHtmlFile is not null)
            {
                await previousHtmlFile.DeleteAsync(StorageDeleteOption.Default);
            }
        }

        var properties = await file.GetBasicPropertiesAsync();
        song.LastModified = properties.DateModified;
        return true;
    }

    private static async Task<string> BuildUniqueCopyFileNameAsync(StorageFolder folder, string baseFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(baseFileName);
        var ext = Path.GetExtension(baseFileName);

        var candidate = $"{baseName} (Copy){ext}";
        var i = 2;

        while (await folder.TryGetItemAsync(candidate) is not null)
        {
            candidate = $"{baseName} (Copy {i}){ext}";
            i++;
        }

        return candidate;
    }

    private static async Task ExportHtmlCompanionAsync(SongDocument song, StorageFolder folder)
    {
        if (string.IsNullOrWhiteSpace(song.FileName))
        {
            return;
        }

        var htmlName = Path.ChangeExtension(song.FileName, ".html");
        var htmlContent = ChordProHtmlExporter.BuildHtml(song.ChordPro);
        var htmlFile = await folder.CreateFileAsync(htmlName, CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(htmlFile, htmlContent);
    }

    private static void EnsureCreatorDirective(SongDocument song)
    {
        var lines = song.ChordPro.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var line in lines)
        {
            if (TryParseDirective(line, CreatorDirective, out var creator))
            {
                song.CreatedBy = creator;
                return;
            }
        }

        var currentUser = string.IsNullOrWhiteSpace(Environment.UserName)
            ? "Unknown"
            : Environment.UserName.Trim();

        song.CreatedBy = currentUser;
        var creatorLine = $"{{{CreatorDirective}: {currentUser}}}";
        song.ChordPro = string.IsNullOrWhiteSpace(song.ChordPro)
            ? creatorLine + "\n"
            : creatorLine + "\n" + song.ChordPro;
    }

    private static async Task<StorageFolder?> GetOrPromptForSongFolderAsync(SongLibraryMode mode)
    {
        var existing = await TryGetStoredFolderAsync(mode);
        if (existing is not null)
        {
            return existing;
        }

        var configured = await ConfigureLibraryFolderAsync(mode);
        if (!configured)
        {
            return null;
        }

        return await TryGetStoredFolderAsync(mode);
    }

    public static async Task<StorageFolder?> TryGetStoredFolderAsync(SongLibraryMode mode)
    {
        if (ApplicationData.Current.LocalSettings.Values[GetFolderTokenKey(mode)] is not string token || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            return await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(token);
        }
        catch
        {
            ApplicationData.Current.LocalSettings.Values.Remove(GetFolderTokenKey(mode));
            return null;
        }
    }

    private static string GetFolderTokenKey(SongLibraryMode mode)
    {
        return mode == SongLibraryMode.Shared
            ? SharedFolderTokenSettingKey
            : LocalFolderTokenSettingKey;
    }

    private static async Task<bool> EnsureExampleSongSeededOnceAsync(StorageFolder folder, SongLibraryMode mode)
    {
        var seedKey = GetExampleSeedKey(mode);
        if (ApplicationData.Current.LocalSettings.Values[seedKey] is bool seeded && seeded)
        {
            return false;
        }

        var exampleFileName = BuildFileName(ExampleSongTitle);
        var existing = await folder.TryGetItemAsync(exampleFileName);
        if (existing is null)
        {
            var file = await folder.CreateFileAsync(exampleFileName, CreationCollisionOption.FailIfExists);
            await FileIO.WriteTextAsync(file, GetExampleSongChordPro());
            ApplicationData.Current.LocalSettings.Values[seedKey] = true;
            return true;
        }

        ApplicationData.Current.LocalSettings.Values[seedKey] = true;
        return false;
    }

    private static string GetExampleSeedKey(SongLibraryMode mode)
    {
        return mode == SongLibraryMode.Shared
            ? ExampleSeededSharedSettingKey
            : ExampleSeededLocalSettingKey;
    }

    private static string GetExampleSongChordPro()
    {
        return """
{x_creator: Lyrical Demo}
{title: Example Song - ChordPro Guide}
{subtitle: Demonstrates common directives and formatting}
{artist: Lyrical}
{key: C}
{capo: 2}
{tempo: 96}
{time: 4/4}
{duration: 3:20}

{comment: Intro comment line}
{ci: This is an italic comment}
{cb: This is a boxed comment}

{define: Cadd9 base-fret 1 frets x 3 2 0 3 3}

{sov: Verse 1}
[C]This is a [G]verse line with [Am]inline [F]chords
[*N.C.]Annotations use an asterisk in the chord token
{eov}

{soc: Chorus}
[F]This is the [G]chorus, sing it [C]loud
[Am]Chord hover should show [F]diagram tooltips
{eoc}

{sob: Bridge}
[Dm]Bridge lines can [G]flow the same [C]way
{eob}

{sot}
e|----------------|
B|----1-----1-----|
G|--0-----0---0---|
D|----------------|
A|----------------|
E|----------------|
{eot}

{c: End of example}
""";
    }

    private static async Task LoadSongsRecursiveAsync(StorageFolder folder, string relativeFolderPath, List<SongDocument> songs)
    {
        var files = await folder.GetFilesAsync();
        foreach (var file in files.Where(IsSongFile))
        {
            var content = await FileIO.ReadTextAsync(file);
            var song = CreateSongFromChordPro(content);
            song.FileName = file.Name;
            song.RelativeFolderPath = relativeFolderPath;
            var properties = await file.GetBasicPropertiesAsync();
            song.LastModified = properties.DateModified;
            songs.Add(song);
        }

        var childFolders = await folder.GetFoldersAsync();
        foreach (var childFolder in childFolders.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            await LoadSongsRecursiveAsync(childFolder, CombineRelativePath(relativeFolderPath, childFolder.Name), songs);
        }
    }

    private static async Task<int> PopulateFolderTreeAsync(StorageFolder folder, SongFolder node)
    {
        var files = await folder.GetFilesAsync();
        node.DirectSongCount = files.Count(IsSongFile);
        var totalSongCount = node.DirectSongCount;

        var childFolders = await folder.GetFoldersAsync();
        foreach (var childFolder in childFolders.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var childNode = new SongFolder
            {
                Name = childFolder.Name,
                RelativePath = CombineRelativePath(node.RelativePath, childFolder.Name)
            };

            node.Children.Add(childNode);
            totalSongCount += await PopulateFolderTreeAsync(childFolder, childNode);
        }

        node.TotalSongCount = totalSongCount;
        return totalSongCount;
    }

    private static SongDocument CreateSongFromChordPro(string chordPro)
    {
        var song = SongDocument.CreateNew();
        song.ChordPro = chordPro;
        ApplyMetadataFromChordPro(song);
        return song;
    }

    private static void ApplyMetadataFromChordPro(SongDocument song)
    {
        var lines = song.ChordPro.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var line in lines)
        {
            if (TryParseDirective(line, "title", out var title) || TryParseDirective(line, "t", out title))
            {
                song.Title = title;
            }

            if (TryParseDirective(line, "artist", out var artist) || TryParseDirective(line, "subtitle", out artist))
            {
                song.Artist = artist;
            }

            if (TryParseDirective(line, "key", out var key))
            {
                song.Key = key;
            }

            if (TryParseDirective(line, CreatorDirective, out var creator))
            {
                song.CreatedBy = creator;
            }
        }
    }

    private static bool TryParseDirective(string line, string directiveName, out string value)
    {
        value = string.Empty;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            return false;
        }

        var inner = trimmed[1..^1];
        var separatorIndex = inner.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return false;
        }

        var name = inner[..separatorIndex].Trim();
        if (!string.Equals(name, directiveName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = inner[(separatorIndex + 1)..].Trim();
        return value.Length > 0;
    }

    private static string UpsertDirective(string chordPro, string directiveName, string value)
    {
        var lines = chordPro.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        var replacement = $"{{{directiveName}: {value}}}";
        var replaced = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (!Regex.IsMatch(line, $@"^\{{\s*(?:{Regex.Escape(directiveName)}|t)\s*:", RegexOptions.IgnoreCase))
            {
                continue;
            }

            if (!replaced)
            {
                lines[i] = replacement;
                replaced = true;
            }
            else
            {
                lines.RemoveAt(i);
                i--;
            }
        }

        if (!replaced)
        {
            lines.Insert(0, replacement);
        }

        return string.Join("\n", lines);
    }

    private static async Task<StorageFolder?> GetFolderByRelativePathAsync(StorageFolder rootFolder, string relativePath, bool createIfMissing = false)
    {
        var normalizedPath = NormalizeRelativeFolderPath(relativePath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return rootFolder;
        }

        StorageFolder current = rootFolder;
        foreach (var segment in normalizedPath.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (createIfMissing)
            {
                current = await current.CreateFolderAsync(segment, CreationCollisionOption.OpenIfExists);
                continue;
            }

            var next = await current.TryGetItemAsync(segment);
            if (next is not StorageFolder nextFolder)
            {
                return null;
            }

            current = nextFolder;
        }

        return current;
    }

    private static async Task<StorageFile?> TryGetSongFileAsync(StorageFolder rootFolder, SongDocument song)
    {
        return await TryGetStorageFileAsync(rootFolder, song.RelativeFilePath);
    }

    private static async Task<StorageFile?> TryGetStorageFileAsync(StorageFolder rootFolder, string? relativeFilePath)
    {
        if (string.IsNullOrWhiteSpace(relativeFilePath))
        {
            return null;
        }

        var normalizedPath = relativeFilePath.Replace('/', '\\').Trim('\\');
        var folderPath = NormalizeRelativeFolderPath(Path.GetDirectoryName(normalizedPath) ?? string.Empty);
        var fileName = Path.GetFileName(normalizedPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var folder = await GetFolderByRelativePathAsync(rootFolder, folderPath);
        if (folder is null)
        {
            return null;
        }

        var item = await folder.TryGetItemAsync(fileName);
        return item as StorageFile;
    }

    private static bool IsSongFile(StorageFile file)
    {
        return string.Equals(file.FileType, ".cho", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativeFolderPath(string? relativeFolderPath)
    {
        if (string.IsNullOrWhiteSpace(relativeFolderPath))
        {
            return string.Empty;
        }

        return relativeFolderPath
            .Trim()
            .Replace('/', '\\')
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

    private static string BuildFolderName(string name)
    {
        var fallback = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        if (fallback.Length == 0)
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        return new string(fallback.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private static string BuildFileName(string title)
    {
        var fallback = string.IsNullOrWhiteSpace(title) ? "Untitled Song" : title.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fallback.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return $"{sanitized}.cho";
    }
}
