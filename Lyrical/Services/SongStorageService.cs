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

        var seeded = await EnsureExampleSongSeededOnceAsync(folder, ActiveLibraryMode);

        var files = await folder.GetFilesAsync();
        var songFiles = files.Where(f => string.Equals(f.FileType, ".cho", StringComparison.OrdinalIgnoreCase)).ToList();

        if (seeded)
        {
            files = await folder.GetFilesAsync();
            songFiles = files.Where(f => string.Equals(f.FileType, ".cho", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var songs = new List<SongDocument>();
        foreach (var file in songFiles)
        {
            var content = await FileIO.ReadTextAsync(file);
            var song = CreateSongFromChordPro(content);
            song.FileName = file.Name;
            var properties = await file.GetBasicPropertiesAsync();
            song.LastModified = properties.DateModified;
            songs.Add(song);
        }

        return songs;
    }

    public static async Task<SongDocument?> LoadSongFromFileAsync(IStorageFile file)
    {
        try
        {
            var content = await FileIO.ReadTextAsync(file);
            var song = CreateSongFromChordPro(content);
            song.FileName = file.Name;
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

        var item = await folder.TryGetItemAsync(song.FileName);
        if (item is not StorageFile file)
        {
            return new SongSaveConflictCheckResult(
                HasConflict: true,
                IsMissing: true,
                CurrentFileLastModified: null,
                CurrentFileContent: string.Empty);
        }

        var properties = await file.GetBasicPropertiesAsync();
        var currentModified = properties.DateModified;

        // Small tolerance for filesystem timestamp precision
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

        var folder = await TryGetStoredFolderAsync(ActiveLibraryMode);
        if (folder is null)
        {
            return false;
        }

        var item = await folder.TryGetItemAsync(song.FileName);
        if (item is not StorageFile file)
        {
            return false;
        }

        await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
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

    private static async Task<bool> SaveSongToFolderAsync(SongDocument song, StorageFolder folder, bool saveAsCopy)
    {
        EnsureCreatorDirective(song);
        ApplyMetadataFromChordPro(song);

        var previousFileName = song.FileName;
        var targetFileName = BuildFileName(song.Title);

        if (saveAsCopy)
        {
            targetFileName = await BuildUniqueCopyFileNameAsync(folder, targetFileName);
        }

        var file = await folder.CreateFileAsync(targetFileName, CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(file, song.ChordPro);
        song.FileName = file.Name;

        if (ExportSettingsService.ExportHtmlOnSave)
        {
            await ExportHtmlCompanionAsync(song, folder);
        }

        // Create timestamped backup after successful save
        if (!saveAsCopy)
        {
            await BackupService.CreateBackupAsync(song.FileName, song.ChordPro);
        }

        if (!saveAsCopy
            && !string.IsNullOrWhiteSpace(previousFileName)
            && !string.Equals(previousFileName, targetFileName, StringComparison.OrdinalIgnoreCase))
        {
            var previousItem = await folder.TryGetItemAsync(previousFileName);
            if (previousItem is StorageFile previousFile)
            {
                await previousFile.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }

            var previousHtmlName = Path.ChangeExtension(previousFileName, ".html");
            var previousHtmlItem = await folder.TryGetItemAsync(previousHtmlName);
            if (previousHtmlItem is StorageFile previousHtmlFile)
            {
                await previousHtmlFile.DeleteAsync(StorageDeleteOption.PermanentDelete);
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

    private static async Task EnsureExampleSongExistsAsync(StorageFolder folder)
    {
        var exampleFileName = BuildFileName(ExampleSongTitle);
        var existing = await folder.TryGetItemAsync(exampleFileName);
        if (existing is not null)
        {
            return;
        }

        var file = await folder.CreateFileAsync(exampleFileName, CreationCollisionOption.FailIfExists);
        await FileIO.WriteTextAsync(file, GetExampleSongChordPro());
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

    private static string BuildFileName(string title)
    {
        var fallback = string.IsNullOrWhiteSpace(title) ? "Untitled Song" : title.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fallback.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return $"{sanitized}.cho";
    }
}
