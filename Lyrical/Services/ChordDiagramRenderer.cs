using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Lyrical.Services;

public static class ChordDiagramRenderer
{
    private static readonly Regex TokenRegex = new("\\[(\\*?)([^\\]]+)\\]", RegexOptions.Compiled);

    private static readonly Dictionary<string, (string Frets, int BaseFret)> KnownChords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["C"] = ("x32010", 1),
            ["Cm"] = ("x35543", 3),
            ["C7"] = ("x32310", 1),
            ["Cmaj7"] = ("x32000", 1),
            ["C#7"] = ("x43404", 3),
            ["D"] = ("xx0232", 1),
            ["Dm"] = ("xx0231", 1),
            ["D7"] = ("xx0212", 1),
            ["Dmaj7"] = ("xx0222", 1),
            ["E"] = ("022100", 1),
            ["Em"] = ("022000", 1),
            ["E7"] = ("020100", 1),
            ["F"] = ("133211", 1),
            ["Fm"] = ("133111", 1),
            ["G"] = ("320003", 1),
            ["G7"] = ("320001", 1),
            ["A"] = ("x02220", 1),
            ["Am"] = ("x02210", 1),
            ["A7"] = ("x02020", 1),
            ["Am7"] = ("x02010", 1),
            ["B"] = ("x24442", 2),
            ["Bm"] = ("x24432", 2),
            ["Bb"] = ("x13331", 1),
            ["F#"] = ("244322", 2),
            ["F#m"] = ("244222", 2),
        };

    public static IReadOnlyList<string> ExtractChords(string? chordPro)
    {
        var chords = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(chordPro))
        {
            return chords;
        }

        foreach (Match match in TokenRegex.Matches(chordPro))
        {
            if (match.Groups[1].Value == "*")
            {
                continue;
            }

            var chord = match.Groups[2].Value.Trim();
            if (chord.Length == 0)
            {
                continue;
            }

            if (seen.Add(chord))
            {
                chords.Add(chord);
            }
        }

        return chords;
    }

    public static UIElement CreateDiagramCard(string chord)
    {
        var definition = ResolveDefinition(chord);

        var root = new StackPanel { Spacing = 6, Width = 96 };
        root.Children.Add(new TextBlock
        {
            Text = chord,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        });

        if (definition is null)
        {
            root.Children.Add(new TextBlock
            {
                Text = "No diagram",
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.65
            });

            return new Border
            {
                Padding = new Thickness(8),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = root
            };
        }

        root.Children.Add(CreateDiagramCanvas(definition.Value.Frets, definition.Value.BaseFret));

        return new Border
        {
            Padding = new Thickness(8),
            BorderBrush = new SolidColorBrush(Colors.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = root
        };
    }

    private static (string Frets, int BaseFret)? ResolveDefinition(string chord)
    {
        var normalized = chord.Trim();

        // User-defined chords take priority over built-ins
        if (CustomChordService.TryGetFretData(normalized, out var customFrets, out var customBase))
        {
            return (customFrets, customBase);
        }

        if (KnownChords.TryGetValue(normalized, out var value))
        {
            return value;
        }

        var slashIndex = normalized.IndexOf('/');
        if (slashIndex > 0)
        {
            var withoutBass = normalized[..slashIndex];

            if (CustomChordService.TryGetFretData(withoutBass, out customFrets, out customBase))
            {
                return (customFrets, customBase);
            }

            if (KnownChords.TryGetValue(withoutBass, out value))
            {
                return value;
            }
        }

        return null;
    }

    private static Canvas CreateDiagramCanvas(string frets, int baseFret)
    {
        var canvas = new Canvas
        {
            Width = 80,
            Height = 112
        };

        const double startX = 10;
        const double startY = 24;
        const double stringSpacing = 12;
        const double fretSpacing = 14;

        for (var s = 0; s < 6; s++)
        {
            var x = startX + (s * stringSpacing);
            canvas.Children.Add(new Line
            {
                X1 = x,
                Y1 = startY,
                X2 = x,
                Y2 = startY + (4 * fretSpacing),
                StrokeThickness = 1,
                Stroke = new SolidColorBrush(Colors.White)
            });
        }

        for (var f = 0; f < 5; f++)
        {
            var y = startY + (f * fretSpacing);
            canvas.Children.Add(new Line
            {
                X1 = startX,
                Y1 = y,
                X2 = startX + (5 * stringSpacing),
                Y2 = y,
                StrokeThickness = f == 0 && baseFret == 1 ? 2 : 1,
                Stroke = new SolidColorBrush(Colors.White)
            });
        }

        if (baseFret > 1)
        {
            canvas.Children.Add(new TextBlock
            {
                Text = baseFret.ToString(),
                Opacity = 0.75
            });
            Canvas.SetLeft(canvas.Children[^1], 0);
            Canvas.SetTop(canvas.Children[^1], startY + 2);
        }

        for (var i = 0; i < Math.Min(6, frets.Length); i++)
        {
            var fretChar = frets[i];
            var x = startX + (i * stringSpacing);

            if (fretChar == 'x' || fretChar == 'X')
            {
                var muted = new TextBlock { Text = "x", Opacity = 0.8 };
                canvas.Children.Add(muted);
                Canvas.SetLeft(muted, x - 3);
                Canvas.SetTop(muted, 2);
                continue;
            }

            if (fretChar == '0')
            {
                var open = new TextBlock { Text = "o", Opacity = 0.8 };
                canvas.Children.Add(open);
                Canvas.SetLeft(open, x - 3);
                Canvas.SetTop(open, 2);
                continue;
            }

            if (!char.IsDigit(fretChar))
            {
                continue;
            }

            var fret = fretChar - '0';
            if (fret <= 0)
            {
                continue;
            }

            var relative = baseFret == 1 ? fret : fret - baseFret + 1;
            if (relative < 1 || relative > 5)
            {
                continue;
            }

            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Colors.Orange)
            };
            canvas.Children.Add(dot);
            Canvas.SetLeft(dot, x - 4);
            Canvas.SetTop(dot, startY + ((relative - 1) * fretSpacing) + (fretSpacing / 2) - 4);
        }

        return canvas;
    }
}
