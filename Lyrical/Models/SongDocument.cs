using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Lyrical.Models;

public partial class SongDocument : INotifyPropertyChanged
{
    private string _title = "Untitled";
    private string _artist = "";
    private string _key = "C";
    private string _chordPro = "";
    private string _createdBy = "Unknown";
    private ChordDiagramPlacement _chordDiagramPlacement = ChordDiagramPlacement.Bottom;
    private string? _fileName;
    private string _relativeFolderPath = string.Empty;
    private DateTimeOffset _lastModified = DateTimeOffset.Now;

    private string _folderLabel = "Location: ";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Artist
    {
        get => _artist;
        set => SetProperty(ref _artist, value);
    }

    public string Key
    {
        get => _key;
        set => SetProperty(ref _key, value);
    }

    public string ChordPro
    {
        get => _chordPro;
        set => SetProperty(ref _chordPro, value);
    }

    public string CreatedBy
    {
        get => _createdBy;
        set => SetProperty(ref _createdBy, string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim());
    }

    public ChordDiagramPlacement ChordDiagramPlacement
    {
        get => _chordDiagramPlacement;
        set => SetProperty(ref _chordDiagramPlacement, value);
    }

    public string? FileName
    {
        get => _fileName;
        set
        {
            if (SetProperty(ref _fileName, value))
            {
                OnPropertyChanged(nameof(RelativeFilePath));
            }
        }
    }

    public string RelativeFolderPath
    {
        get => _relativeFolderPath;
        set
        {
            var normalized = NormalizeRelativeFolderPath(value);
            if (SetProperty(ref _relativeFolderPath, normalized))
            {
                OnPropertyChanged(nameof(FolderDisplayName));
                OnPropertyChanged(nameof(RelativeFilePath));
            }
        }
    }

    public string FolderDisplayName => string.IsNullOrWhiteSpace(RelativeFolderPath)
        ? _folderLabel + "Library Root"
        : _folderLabel + RelativeFolderPath;

    public string RelativeFilePath => string.IsNullOrWhiteSpace(FileName)
        ? RelativeFolderPath
        : string.IsNullOrWhiteSpace(RelativeFolderPath)
            ? FileName
            : $"{RelativeFolderPath}\\{FileName}";

    public DateTimeOffset LastModified
    {
        get => _lastModified;
        set
        {
            if (SetProperty(ref _lastModified, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastModifiedText)));
            }
        }
    }

    public string LastModifiedText => LastModified.LocalDateTime.ToString("M/dd/yyyy h:mm tt", CultureInfo.CurrentCulture);

    public static SongDocument CreateNew(string title = "Untitled", string relativeFolderPath = "")
    {
        return new SongDocument
        {
            Title = title,
            RelativeFolderPath = relativeFolderPath,
            ChordPro = $"{{title: {title}}}\n"
        };
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
}
