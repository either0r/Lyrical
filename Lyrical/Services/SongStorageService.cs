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

public static class SongStorageService
{
    private const string FolderTokenSettingKey = "SongLibraryFolderToken";

    public static async Task<IReadOnlyList<SongDocument>> LoadSongsAsync()
    {
        var folder = await TryGetStoredFolderAsync();
        if (folder is null)
        {
            return [];
        }

        var files = await folder.GetFilesAsync();
        var songFiles = files.Where(f => string.Equals(f.FileType, ".cho", StringComparison.OrdinalIgnoreCase));

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

    public static async Task<bool> SaveSongAsync(SongDocument song)
    {
        var folder = await GetOrPromptForSongFolderAsync();
        if (folder is null)
        {
            return false;
        }

        ApplyMetadataFromChordPro(song);

        var fileName = song.FileName;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = BuildFileName(song.Title);
        }

        var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(file, song.ChordPro);
        song.FileName = file.Name;
        var properties = await file.GetBasicPropertiesAsync();
        song.LastModified = properties.DateModified;
        return true;
    }

    public static async Task<bool> DeleteSongAsync(SongDocument song)
    {
        if (string.IsNullOrWhiteSpace(song.FileName))
        {
            return false;
        }

        var folder = await TryGetStoredFolderAsync();
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

    private static async Task<StorageFolder?> GetOrPromptForSongFolderAsync()
    {
        var existing = await TryGetStoredFolderAsync();
        if (existing is not null)
        {
            return existing;
        }

        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        if (App.MainAppWindow is null)
        {
            return null;
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainAppWindow));
        var selected = await picker.PickSingleFolderAsync();
        if (selected is null)
        {
            return null;
        }

        var token = StorageApplicationPermissions.FutureAccessList.Add(selected);
        ApplicationData.Current.LocalSettings.Values[FolderTokenSettingKey] = token;
        return selected;
    }

    private static async Task<StorageFolder?> TryGetStoredFolderAsync()
    {
        if (ApplicationData.Current.LocalSettings.Values[FolderTokenSettingKey] is not string token || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            return await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(token);
        }
        catch
        {
            ApplicationData.Current.LocalSettings.Values.Remove(FolderTokenSettingKey);
            return null;
        }
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
        var lines = song.ChordPro.Replace("\r\n", "\n").Split('\n');
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
        }
    }

    private static bool TryParseDirective(string line, string directiveName, out string value)
    {
        value = string.Empty;
        var match = Regex.Match(line.Trim(), "^\\{" + Regex.Escape(directiveName) + "\\s*:\\s*(.*?)\\}$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        value = match.Groups[1].Value.Trim();
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
