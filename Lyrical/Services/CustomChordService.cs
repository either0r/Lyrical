using Lyrical.Models;
using System.Collections.Generic;
using System.Text.Json;
using Windows.Storage;

namespace Lyrical.Services;

public static class CustomChordService
{
    private const string SettingsKey = "CustomChordDefinitions";

    private static List<CustomChordDefinition> _definitions = [];

    public static IReadOnlyList<CustomChordDefinition> Definitions => _definitions;

    public static void Load()
    {
        _definitions = [];

        if (ApplicationData.Current.LocalSettings.Values[SettingsKey] is not string json
            || string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var rawList = JsonSerializer.Deserialize<List<string>>(json);
            if (rawList is null)
            {
                return;
            }

            foreach (var raw in rawList)
            {
                if (CustomChordDefinition.TryParse(raw, out var def))
                {
                    _definitions.Add(def);
                }
            }
        }
        catch
        {
            _definitions = [];
        }
    }

    public static bool TryAdd(string rawDirective, out string error)
    {
        error = string.Empty;

        if (!CustomChordDefinition.TryParse(rawDirective, out var def))
        {
            error = "Invalid {define: …} directive. Expected format:\n{define: Name base-fret 1 frets x x x x x x}";
            return false;
        }

        _definitions.RemoveAll(d => string.Equals(d.Name, def.Name, System.StringComparison.OrdinalIgnoreCase));
        _definitions.Add(def);
        Save();
        return true;
    }

    public static bool TryUpdate(CustomChordDefinition existing, string newRawDirective, out string error)
    {
        error = string.Empty;

        if (!CustomChordDefinition.TryParse(newRawDirective, out var def))
        {
            error = "Invalid {define: …} directive. Expected format:\n{define: Name base-fret 1 frets x x x x x x}";
            return false;
        }

        // Remove the original entry, then any collision on the new name
        _definitions.Remove(existing);
        _definitions.RemoveAll(d => string.Equals(d.Name, def.Name, System.StringComparison.OrdinalIgnoreCase));
        _definitions.Add(def);
        Save();
        return true;
    }

    public static void Remove(CustomChordDefinition definition)
    {
        _definitions.Remove(definition);
        Save();
    }

    public static bool TryGetFretData(string chordName, out string frets, out int baseFret)
    {
        frets = string.Empty;
        baseFret = 1;

        var def = _definitions.Find(d => string.Equals(d.Name, chordName, System.StringComparison.OrdinalIgnoreCase));
        if (def is null)
        {
            return false;
        }

        frets = def.Frets;
        baseFret = def.BaseFret;
        return true;
    }

    private static void Save()
    {
        var rawList = _definitions.ConvertAll(d => d.RawDirective);
        ApplicationData.Current.LocalSettings.Values[SettingsKey] = JsonSerializer.Serialize(rawList);
    }
}
