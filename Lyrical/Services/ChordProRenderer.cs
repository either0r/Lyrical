using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Lyrical.Services;

public static partial class ChordProRenderer
{
    private static readonly Regex TokenRegex = new("\\[(\\*?)([^\\]]+)\\]", RegexOptions.Compiled);
    private const string MonospaceFont = "Consolas";

    public static void RenderTo(RichTextBlock target, string? chordPro)
    {
        target.Blocks.Clear();

        if (string.IsNullOrWhiteSpace(chordPro))
        {
            return;
        }

        var inChorus = false;
        var normalized = chordPro.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (TryHandleDirective(line, target, ref inChorus))
            {
                continue;
            }

            RenderLyricLine(target, line, inChorus);
        }
    }

    private static bool TryHandleDirective(string line, RichTextBlock target, ref bool inChorus)
    {
        if (!TryParseDirective(line, out var name, out var value))
        {
            return false;
        }

        switch (name)
        {
            case "soc":
            case "start_of_chorus":
                inChorus = true;
                return true;
            case "eoc":
            case "end_of_chorus":
                inChorus = false;
                return true;
            case "title":
            case "t":
                AddMetaParagraph(target, value, 28, FontWeights.SemiBold);
                return true;
            case "subtitle":
            case "st":
            case "artist":
                AddMetaParagraph(target, value, 16, FontWeights.Medium);
                return true;
            case "comment":
            case "c":
                AddMetaParagraph(target, value, 14, FontWeights.Normal, italic: true);
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseDirective(string line, out string name, out string value)
    {
        name = string.Empty;
        value = string.Empty;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            return false;
        }

        var inner = trimmed[1..^1].Trim();
        if (inner.Length == 0)
        {
            return false;
        }

        var separatorIndex = inner.IndexOf(':');
        if (separatorIndex < 0)
        {
            name = inner.ToLowerInvariant();
            return true;
        }

        name = inner[..separatorIndex].Trim().ToLowerInvariant();
        value = inner[(separatorIndex + 1)..].Trim();
        return true;
    }

    private static void AddMetaParagraph(RichTextBlock target, string text, double size, Windows.UI.Text.FontWeight weight, bool italic = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var paragraph = new Paragraph();
        var run = new Run
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            FontStyle = italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal
        };
        paragraph.Inlines.Add(run);
        target.Blocks.Add(paragraph);
    }

    private static void RenderLyricLine(RichTextBlock target, string line, bool inChorus)
    {
        var paragraph = new Paragraph();
        if (inChorus)
        {
            paragraph.Margin = new Thickness(16, 0, 0, 0);
        }

        if (string.IsNullOrEmpty(line))
        {
            paragraph.Inlines.Add(CreateMonospaceRun(" ", Colors.Transparent, FontWeights.Normal));
            target.Blocks.Add(paragraph);
            return;
        }

        var lyricLine = new StringBuilder();
        var chordLine = new StringBuilder();
        var tokens = new List<PlacedToken>();
        var lastIndex = 0;

        foreach (Match match in TokenRegex.Matches(line))
        {
            var lyricText = line[lastIndex..match.Index];
            if (lyricText.Length > 0)
            {
                lyricLine.Append(lyricText);
            }

            var tokenText = match.Groups[2].Value;
            var isAnnotation = match.Groups[1].Value == "*";
            var lyricPosition = lyricLine.Length;
            var placedPosition = PlaceToken(chordLine, lyricPosition, tokenText);
            tokens.Add(new PlacedToken(placedPosition, tokenText, isAnnotation));

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < line.Length)
        {
            lyricLine.Append(line[lastIndex..]);
        }

        if (chordLine.ToString().Trim().Length > 0)
        {
            AddChordInlines(paragraph, chordLine.ToString(), tokens);
            paragraph.Inlines.Add(new LineBreak());
        }

        paragraph.Inlines.Add(CreateMonospaceRun(lyricLine.Length == 0 ? " " : lyricLine.ToString(), Colors.White, FontWeights.Normal));
        target.Blocks.Add(paragraph);
    }

    private static int PlaceToken(StringBuilder chordLine, int requestedPosition, string token)
    {
        var position = requestedPosition;

        while (chordLine.Length < position)
        {
            chordLine.Append(' ');
        }

        while (position < chordLine.Length && chordLine[position] != ' ')
        {
            position++;
        }

        if (chordLine.Length < position + token.Length)
        {
            chordLine.Append(' ', position + token.Length - chordLine.Length);
        }

        for (var i = 0; i < token.Length; i++)
        {
            chordLine[position + i] = token[i];
        }

        return position;
    }

    private static void AddChordInlines(Paragraph paragraph, string chordText, IReadOnlyList<PlacedToken> tokens)
    {
        var current = 0;
        foreach (var token in tokens)
        {
            if (token.Position > current)
            {
                paragraph.Inlines.Add(CreateMonospaceRun(chordText[current..token.Position], Colors.Orange, FontWeights.Bold));
            }

            var span = new Span();
            span.Inlines.Add(CreateMonospaceRun(token.Text, token.IsAnnotation ? Colors.CornflowerBlue : Colors.Orange, token.IsAnnotation ? FontWeights.Normal : FontWeights.Bold));

            if (!token.IsAnnotation)
            {
                ToolTipService.SetToolTip(span, ChordDiagramRenderer.CreateDiagramCard(token.Text));
            }

            paragraph.Inlines.Add(span);
            current = token.Position + token.Text.Length;
        }

        if (current < chordText.Length)
        {
            paragraph.Inlines.Add(CreateMonospaceRun(chordText[current..], Colors.Orange, FontWeights.Bold));
        }
    }

    private static Run CreateMonospaceRun(string text, Windows.UI.Color color, Windows.UI.Text.FontWeight weight)
    {
        return new Run
        {
            Text = text.Replace(" ", "\u00A0"),
            FontFamily = new FontFamily(MonospaceFont),
            Foreground = new SolidColorBrush(color),
            FontWeight = weight
        };
    }

    private readonly record struct PlacedToken(int Position, string Text, bool IsAnnotation);
}
