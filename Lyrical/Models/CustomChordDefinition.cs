using System;
using System.Collections.Generic;
using System.Linq;
using Lyrical.Models;

namespace Lyrical.Models;

/// <summary>Parsed representation of a ChordPro {define: …} directive.</summary>
public class CustomChordDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Raw directive exactly as the user typed it, e.g.
    /// {define: Bes base-fret 1 frets 1 1 3 3 3 1 fingers 1 1 2 3 4 1}
    /// </summary>
    public string RawDirective { get; set; } = string.Empty;

    public int BaseFret { get; set; } = 1;

    /// <summary>Six fret positions, space-separated. Use 0 for open, x/-1 for muted.</summary>
    public string Frets { get; set; } = string.Empty;

    public static bool TryParse(string directive, out CustomChordDefinition result)
    {
        result = new CustomChordDefinition();

        var trimmed = directive.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            return false;
        }

        var inner = trimmed[1..^1].Trim();

        if (!inner.StartsWith("define", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var body = inner[6..].TrimStart(':', ' ');
        var tokens = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            return false;
        }

        result.Name = tokens[0];
        result.RawDirective = trimmed;

        int baseFret = 1;
        var fretsList = new List<string>();
        var i = 1;

        while (i < tokens.Length)
        {
            var token = tokens[i].ToLowerInvariant();

            if ((token == "base-fret" || token == "base_fret") && i + 1 < tokens.Length)
            {
                int.TryParse(tokens[i + 1], out baseFret);
                i += 2;
                continue;
            }

            if (token == "frets")
            {
                i++;
                while (i < tokens.Length
                    && tokens[i].ToLowerInvariant() != "fingers"
                    && tokens[i].ToLowerInvariant() != "keys"
                    && tokens[i].ToLowerInvariant() != "display"
                    && tokens[i].ToLowerInvariant() != "format"
                    && tokens[i].ToLowerInvariant() != "diagram"
                    && tokens[i].ToLowerInvariant() != "copy"
                    && tokens[i].ToLowerInvariant() != "copyall")
                {
                    fretsList.Add(tokens[i]);
                    i++;
                }
                continue;
            }

            // Skip all other keywords and their values (fingers, keys, display, format, etc.)
            i++;
        }

        result.BaseFret = baseFret;
        result.Frets = string.Join("", fretsList.Select(NormalizeFretChar));
        return result.Frets.Length > 0;
    }

    private static string NormalizeFretChar(string token)
    {
        if (token == "x" || token == "X" || token == "-1" || token == "N")
        {
            return "x";
        }

        if (int.TryParse(token, out var n) && n >= 0 && n <= 9)
        {
            return n.ToString();
        }

        return "x";
    }
}
