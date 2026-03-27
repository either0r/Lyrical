using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace Lyrical.Services;

public static class BackupService
{
    private const string HistoryFolderName = ".history";
    private const int MaxBackupsPerSong = 10;

    public static async Task CreateBackupAsync(string fileName, string content)
    {
        try
        {
            var historyFolder = await GetOrCreateHistoryFolderAsync();
            if (historyFolder is null)
            {
                return;
            }

            var baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
            var ext = System.IO.Path.GetExtension(fileName);
            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH-mm-ss");
            var backupFileName = $"{baseName}~{timestamp}{ext}";

            var backupFile = await historyFolder.CreateFileAsync(backupFileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(backupFile, content);

            await CleanupOldBackupsAsync(historyFolder, baseName, ext);
        }
        catch
        {
            // Backup failure should not block normal save
        }
    }

    public static async Task<IReadOnlyList<BackupInfo>> GetBackupsAsync(string fileName)
    {
        try
        {
            var historyFolder = await GetHistoryFolderIfExistsAsync();
            if (historyFolder is null)
            {
                return [];
            }

            var baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
            var ext = System.IO.Path.GetExtension(fileName);
            var pattern = $"{baseName}~";

            var files = await historyFolder.GetFilesAsync();
            var backups = files
                .Where(f => f.Name.StartsWith(pattern) && f.Name.EndsWith(ext))
                .OrderByDescending(f => f.Name)
                .Select((f, idx) => new BackupInfo(f.Name, ExtractTimestamp(f.Name), idx + 1))
                .ToList();

            return backups;
        }
        catch
        {
            return [];
        }
    }

    public static async Task<string?> RestoreBackupAsync(string backupFileName)
    {
        try
        {
            var historyFolder = await GetHistoryFolderIfExistsAsync();
            if (historyFolder is null)
            {
                return null;
            }

            var item = await historyFolder.TryGetItemAsync(backupFileName);
            if (item is not StorageFile file)
            {
                return null;
            }

            return await FileIO.ReadTextAsync(file);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<StorageFolder?> GetOrCreateHistoryFolderAsync()
    {
        try
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            var existing = await localFolder.TryGetItemAsync(HistoryFolderName);
            if (existing is StorageFolder folder)
            {
                return folder;
            }

            return await localFolder.CreateFolderAsync(HistoryFolderName, CreationCollisionOption.OpenIfExists);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<StorageFolder?> GetHistoryFolderIfExistsAsync()
    {
        try
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            var existing = await localFolder.TryGetItemAsync(HistoryFolderName);
            return existing as StorageFolder;
        }
        catch
        {
            return null;
        }
    }

    private static async Task CleanupOldBackupsAsync(StorageFolder historyFolder, string baseName, string ext)
    {
        try
        {
            var pattern = $"{baseName}~";
            var files = await historyFolder.GetFilesAsync();
            var matching = files
                .Where(f => f.Name.StartsWith(pattern) && f.Name.EndsWith(ext))
                .OrderByDescending(f => f.Name)
                .Skip(MaxBackupsPerSong)
                .ToList();

            foreach (var file in matching)
            {
                await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }
        }
        catch
        {
            // Cleanup failure is non-critical
        }
    }

    private static string ExtractTimestamp(string fileName)
    {
        var start = fileName.LastIndexOf('~');
        if (start < 0)
        {
            return string.Empty;
        }

        var end = fileName.LastIndexOf('.');
        if (end <= start)
        {
            return string.Empty;
        }

        var raw = fileName[(start + 1)..end];
        // Parse "2025-01-15T14-32-45" → human readable
        if (raw.Length >= 10)
        {
            var date = raw[..10]; // "2025-01-15"
            var time = raw[11..].Replace('-', ':'); // "14:32:45"
            return $"{date} {time}";
        }

        return raw;
    }
}

public sealed record BackupInfo(string FileName, string Timestamp, int Index);
