using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Lyrical.Services;

public static partial class ChordProRenderer
{
    private static readonly Regex TokenRegex = new("\\[(\\*?)([^\\]]+)\\]", RegexOptions.Compiled);
    private static readonly Regex LabelAttrRegex = new("label\\s*=\\s*[\"']([^\"']*)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private const string MonospaceFont = "Consolas";

    private enum SectionType { None, Chorus, Verse, Bridge, Tab }

    public static void RenderTo(RichTextBlock target, string? chordPro)
    {
        target.Blocks.Clear();

        if (string.IsNullOrWhiteSpace(chordPro))
        {
            return;
        }

        var section = SectionType.None;
        var normalized = chordPro.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (TryHandleDirective(line, target, ref section))
            {
                continue;
            }

            RenderLyricLine(target, line, section);
        }
    }

    // ── Directive parsing ─────────────────────────────────────────────────────

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

        // Name ends at first ':' or first ' ', whichever comes first
        var colonIdx = inner.IndexOf(':');
        var spaceIdx = inner.IndexOf(' ');

        int nameEnd;
        if (colonIdx < 0 && spaceIdx < 0)       nameEnd = inner.Length;
        else if (colonIdx < 0)                   nameEnd = spaceIdx;
        else if (spaceIdx < 0)                   nameEnd = colonIdx;
        else                                     nameEnd = Math.Min(colonIdx, spaceIdx);

        var rawName = inner[..nameEnd].Trim().ToLowerInvariant();

        // Strip conditional selector suffix: start_of_verse-soprano → start_of_verse
        var dashIdx = rawName.LastIndexOf('-');
        if (dashIdx > 0)
        {
            rawName = rawName[..dashIdx];
        }

        name = rawName;

        if (nameEnd < inner.Length)
        {
            value = inner[nameEnd..].TrimStart(':', ' ').Trim();
        }

        return true;
    }

    private static string ExtractLabel(string value)
    {
        var match = LabelAttrRegex.Match(value);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // Plain value with no attribute syntax is the label directly
        return value.Contains('=') ? string.Empty : value;
    }

    // ── Directive handling ────────────────────────────────────────────────────

    private static bool TryHandleDirective(string line, RichTextBlock target, ref SectionType section)
    {
        if (!TryParseDirective(line, out var name, out var value))
        {
            return false;
        }

        // Always silently ignore custom x_ directives
        if (name.StartsWith("x_"))
        {
            return true;
        }

        var label = ExtractLabel(value);

        switch (name)
        {
            // ── Preamble ──────────────────────────────────────────────────────
            case "new_song":
            case "ns":
                target.Blocks.Clear();
                section = SectionType.None;
                return true;

            // ── Meta-data — rendered visually ─────────────────────────────────
            case "title":
            case "t":
                AddMetaParagraph(target, value, 26, FontWeights.SemiBold);
                return true;

            case "subtitle":
            case "st":
                AddMetaParagraph(target, value, 18, FontWeights.Medium);
                return true;

            case "artist":
                AddMetaParagraph(target, value, 16, FontWeights.Medium);
                return true;

            case "composer":
            case "lyricist":
                AddMetaParagraph(target, value, 14, FontWeights.Normal);
                return true;

            case "album":
                AddMetaParagraph(target, $"Album: {value}", 12, FontWeights.Normal, italic: true);
                return true;

            case "year":
                AddMetaParagraph(target, $"Year: {value}", 12, FontWeights.Normal, italic: true);
                return true;

            case "copyright":
                AddMetaParagraph(target, $"© {value}", 11, FontWeights.Normal, italic: true);
                return true;

            case "key":
                AddMetaParagraph(target, $"Key: {value}", 12, FontWeights.Normal);
                return true;

            case "capo":
                AddMetaParagraph(target, $"Capo: {value}", 12, FontWeights.Normal);
                return true;

            case "tempo":
                AddMetaParagraph(target, $"Tempo: {value} bpm", 12, FontWeights.Normal);
                return true;

            case "time":
                AddMetaParagraph(target, $"Time: {value}", 12, FontWeights.Normal);
                return true;

            case "duration":
                AddMetaParagraph(target, $"Duration: {value}", 12, FontWeights.Normal, italic: true);
                return true;

            // ── Meta-data — silent (index/sort only) ──────────────────────────
            case "sorttitle":
            case "sortartist":
            case "tag":
            case "meta":
                return true;

            // ── Formatting directives ─────────────────────────────────────────
            case "comment":
            case "c":
            case "highlight":
                AddCommentParagraph(target, value, italic: false, box: false);
                return true;

            case "comment_italic":
            case "ci":
                AddCommentParagraph(target, value, italic: true, box: false);
                return true;

            case "comment_box":
            case "cb":
                AddCommentParagraph(target, value, italic: false, box: true);
                return true;

            // ── Environment: Chorus ───────────────────────────────────────────
            case "start_of_chorus":
            case "soc":
                section = SectionType.Chorus;
                AddSectionLabelParagraph(target, string.IsNullOrWhiteSpace(label) ? "Chorus" : label, SectionType.Chorus);
                return true;

            case "end_of_chorus":
            case "eoc":
                section = SectionType.None;
                return true;

            case "chorus":
                AddSectionLabelParagraph(target, string.IsNullOrWhiteSpace(label) ? "Chorus" : label, SectionType.Chorus);
                return true;

            // ── Environment: Verse ────────────────────────────────────────────
            case "start_of_verse":
            case "sov":
                section = SectionType.Verse;
                AddSectionLabelParagraph(target, string.IsNullOrWhiteSpace(label) ? "Verse" : label, SectionType.Verse);
                return true;

            case "end_of_verse":
            case "eov":
                section = SectionType.None;
                return true;

            // ── Environment: Bridge ───────────────────────────────────────────
            case "start_of_bridge":
            case "sob":
                section = SectionType.Bridge;
                AddSectionLabelParagraph(target, string.IsNullOrWhiteSpace(label) ? "Bridge" : label, SectionType.Bridge);
                return true;

            case "end_of_bridge":
            case "eob":
                section = SectionType.None;
                return true;

            // ── Environment: Tab ──────────────────────────────────────────────
            case "start_of_tab":
            case "sot":
                section = SectionType.Tab;
                return true;

            case "end_of_tab":
            case "eot":
                section = SectionType.None;
                return true;

            // ── Environment: Grid (skip content) ─────────────────────────────
            case "start_of_grid":
            case "sog":
            case "end_of_grid":
            case "eog":
                return true;

            // ── Chord definition (handled by diagram service) ─────────────────
            case "define":
            case "chord":
                return true;

            // ── Output / layout directives — silent ───────────────────────────
            case "new_page":
            case "np":
            case "new_physical_page":
            case "npp":
            case "column_break":
            case "colb":
            case "pagetype":
            case "diagrams":
            case "grid":
            case "g":
            case "no_grid":
            case "ng":
            case "titles":
            case "columns":
            case "col":
                return true;

            // ── Font/colour/style directives — silent ─────────────────────────
            case "chordfont":    case "cf":
            case "chordsize":   case "cs":
            case "chordcolour":
            case "chorusfont":  case "chorussize":  case "choruscolour":
            case "textfont":    case "tf":
            case "textsize":    case "ts":
            case "textcolour":
            case "titlefont":   case "titlesize":   case "titlecolour":
            case "tabfont":     case "tabsize":     case "tabcolour":
            case "footerfont":  case "footersize":  case "footercolour":
            case "gridfont":    case "gridsize":    case "gridcolour":
            case "labelfont":   case "labelsize":   case "labelcolour":
            case "tocfont":     case "tocsize":     case "toccolour":
                return true;

            // ── Transposition — silent ────────────────────────────────────────
            case "transpose":
                return true;

            // ── All unknown directives: silently skip (never render as lyrics) ─
            default:
                return true;
        }
    }

    // ── Visual helpers ────────────────────────────────────────────────────────

    private static void AddMetaParagraph(RichTextBlock target, string text, double size, Windows.UI.Text.FontWeight weight, bool italic = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            FontStyle = italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal
        });
        target.Blocks.Add(paragraph);
    }

    private static void AddCommentParagraph(RichTextBlock target, string text, bool italic, bool box)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var paragraph = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };
        paragraph.Inlines.Add(new Run
        {
            Text = box ? $"[ {text} ]" : text,
            FontFamily = new FontFamily(MonospaceFont),
            FontStyle = italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            Foreground = new SolidColorBrush(Colors.LightGray)
        });
        target.Blocks.Add(paragraph);
    }

    private static void AddSectionLabelParagraph(RichTextBlock target, string label, SectionType section)
    {
        var color = section switch
        {
            SectionType.Chorus => Colors.CornflowerBlue,
            SectionType.Bridge => Colors.MediumPurple,
            _ => Colors.DarkGray
        };

        var paragraph = new Paragraph { Margin = new Thickness(0, 6, 0, 2) };
        paragraph.Inlines.Add(new Run
        {
            Text = label.ToUpperInvariant(),
            FontWeight = FontWeights.Bold,
            FontStyle = Windows.UI.Text.FontStyle.Normal,
            Foreground = new SolidColorBrush(color)
        });
        target.Blocks.Add(paragraph);
    }

    // ── Lyric line rendering ──────────────────────────────────────────────────

    private static void RenderLyricLine(RichTextBlock target, string line, SectionType section)
    {
        var paragraph = new Paragraph();

        var leftIndent = section switch
        {
            SectionType.Chorus => 16.0,
            SectionType.Bridge => 8.0,
            _ => 0.0
        };

        if (leftIndent > 0)
        {
            paragraph.Margin = new Thickness(leftIndent, 0, 0, 0);
        }

        if (string.IsNullOrEmpty(line))
        {
            paragraph.Inlines.Add(CreateMonospaceRun(" ", Colors.Transparent, FontWeights.Normal));
            target.Blocks.Add(paragraph);
            return;
        }

        // Tab sections render raw monospace in a distinct colour
        if (section == SectionType.Tab)
        {
            paragraph.Inlines.Add(CreateMonospaceRun(line, Colors.LightGreen, FontWeights.Normal));
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
