using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lyrical.Models;

public class SongDocument : INotifyPropertyChanged
{
    private string _title = "Untitled";
    private string _artist = "";
    private string _key = "C";
    private string _chordPro = "{title: Untitled}\n{artist: }\n\n[Am]Write your [F]first line [C]here";
    private ChordDiagramPlacement _chordDiagramPlacement = ChordDiagramPlacement.Bottom;
    private string? _fileName;
    private DateTimeOffset _lastModified = DateTimeOffset.Now;

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

    public ChordDiagramPlacement ChordDiagramPlacement
    {
        get => _chordDiagramPlacement;
        set => SetProperty(ref _chordDiagramPlacement, value);
    }

    public string? FileName
    {
        get => _fileName;
        set => SetProperty(ref _fileName, value);
    }

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

    public string LastModifiedText => LastModified.LocalDateTime.ToString("M/dd/yyyy h:mm tt");

    public static SongDocument CreateNew()
    {
        return new SongDocument();
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
}
