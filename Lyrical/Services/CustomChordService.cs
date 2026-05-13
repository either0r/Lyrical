using Lyrical.Models;
using System.Collections.Generic;
using System.Linq;
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

    public static void ImportFromChordPro(string? chordPro)
    {
        var imported = false;

        foreach (var definition in GetDefinitionsFromChordPro(chordPro))
        {
            _definitions.RemoveAll(d => string.Equals(d.Name, definition.Name, System.StringComparison.OrdinalIgnoreCase));
            _definitions.Add(definition);
            imported = true;
        }

        if (imported)
        {
            Save();
        }
    }

    public static string EmbedUsedDefinitions(string? chordPro)
    {
        var normalized = chordPro?.Replace("\r\n", "\n").Replace('\r', '\n') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        var lines = normalized.Split('\n').ToList();
        var usedChordNames = ChordDiagramRenderer.ExtractChords(normalized);
        if (usedChordNames.Count == 0)
        {
            return normalized;
        }

        var embeddedDefinitions = GetDefinitionsFromChordPro(normalized)
            .Select(d => d.Name)
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        var definitionsToInsert = new List<string>();
        foreach (var chordName in usedChordNames)
        {
            var matchingDefinition = _definitions.FirstOrDefault(d => string.Equals(d.Name, chordName, System.StringComparison.OrdinalIgnoreCase));
            if (matchingDefinition is null || !embeddedDefinitions.Add(matchingDefinition.Name))
            {
                continue;
            }

            definitionsToInsert.Add(matchingDefinition.RawDirective);
        }

        if (definitionsToInsert.Count == 0)
        {
            return normalized;
        }

        var insertIndex = 0;
        while (insertIndex < lines.Count)
        {
            var trimmed = lines[insertIndex].Trim();
            if (string.IsNullOrWhiteSpace(trimmed)
                || IsDirective(trimmed, "title")
                || IsDirective(trimmed, "t")
                || IsDirective(trimmed, "subtitle")
                || IsDirective(trimmed, "artist")
                || IsDirective(trimmed, "album")
                || IsDirective(trimmed, "year")
                || IsDirective(trimmed, "key")
                || IsDirective(trimmed, "tempo")
                || IsDirective(trimmed, "capo")
                || IsDirective(trimmed, "x_creator"))
            {
                insertIndex++;
                continue;
            }

            break;
        }

        lines.InsertRange(insertIndex, definitionsToInsert);
        return string.Join("\n", lines);
    }

    private static IEnumerable<CustomChordDefinition> GetDefinitionsFromChordPro(string? chordPro)
    {
        var normalized = chordPro?.Replace("\r\n", "\n").Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            yield break;
        }

        foreach (var line in normalized.Split('\n'))
        {
            if (CustomChordDefinition.TryParse(line, out var definition))
            {
                yield return definition;
            }
        }
    }

    private static bool IsDirective(string line, string directiveName)
    {
        if (!line.StartsWith('{') || !line.EndsWith('}'))
        {
            return false;
        }

        var inner = line[1..^1].Trim();
        var separatorIndex = inner.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return false;
        }

        var name = inner[..separatorIndex].Trim();
        return string.Equals(name, directiveName, System.StringComparison.OrdinalIgnoreCase);
    }

    private static void Save()
    {
        var rawList = _definitions.ConvertAll(d => d.RawDirective);
        ApplicationData.Current.LocalSettings.Values[SettingsKey] = JsonSerializer.Serialize(rawList);
    }
}
