using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Lyrical.Services;

public static class ChordProHtmlExporter
{
    private static readonly Regex ChordTokenRegex = new("\\[(\\*?)([^\\]]+)\\]", RegexOptions.Compiled);

    private enum SectionType { None, Chorus, Verse, Bridge, Tab }

    public static string BuildHtml(string? chordPro)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html><head><meta charset='utf-8' />");
        sb.AppendLine("<meta name='viewport' content='width=device-width,initial-scale=1' />");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;background:#121212;color:#f2f2f2;margin:24px;line-height:1.45}");
        sb.AppendLine("h1{font-size:2rem;margin:.2rem 0 .4rem 0}");
        sb.AppendLine("h2{font-size:1.2rem;margin:.1rem 0 .25rem 0;color:#cfd8dc;font-weight:600}");
        sb.AppendLine(".meta{opacity:.85;margin:.15rem 0}");
        sb.AppendLine(".comment{opacity:.78;font-style:italic;margin:.3rem 0}");
        sb.AppendLine(".comment-box{display:inline-block;border:1px solid #666;padding:.1rem .4rem;border-radius:4px}");
        sb.AppendLine(".section{font-weight:700;letter-spacing:.04em;margin:.8rem 0 .35rem 0}");
        sb.AppendLine(".section.chorus{color:#8ab4f8}");
        sb.AppendLine(".section.bridge{color:#c58af9}");
        sb.AppendLine(".section.verse{color:#a0a0a0}");
        sb.AppendLine(".line{margin:.2rem 0;white-space:pre-wrap}");
        sb.AppendLine(".line.chorus{padding-left:16px}");
        sb.AppendLine(".line.bridge{padding-left:8px}");
        sb.AppendLine(".chord{color:#ffb74d;font-weight:700}");
        sb.AppendLine(".annotation{color:#8ab4f8}");
        sb.AppendLine("pre.tab{font-family:Consolas,monospace;color:#b9f6ca;margin:.25rem 0}");
        sb.AppendLine("</style></head><body>");

        if (!string.IsNullOrWhiteSpace(chordPro))
        {
            var section = SectionType.None;
            var lines = chordPro.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();
                if (line.TrimStart().StartsWith('#'))
                {
                    continue;
                }

                if (TryHandleDirective(line, ref section, sb))
                {
                    continue;
                }

                RenderLyricLine(line, section, sb);
            }
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static bool TryHandleDirective(string line, ref SectionType section, StringBuilder sb)
    {
        if (!TryParseDirective(line, out var name, out var value))
        {
            return false;
        }

        var label = ExtractLabel(value);

        switch (name)
        {
            case "title":
            case "t":
                if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"<h1>{Encode(value)}</h1>");
                return true;
            case "subtitle":
            case "st":
                if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"<h2>{Encode(value)}</h2>");
                return true;
            case "artist":
                if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"<div class='meta'>Artist: {Encode(value)}</div>");
                return true;
            case "key":
                if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"<div class='meta'>Key: {Encode(value)}</div>");
                return true;
            case "capo":
                if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"<div class='meta'>Capo: {Encode(value)}</div>");
                return true;
            case "tempo":
                if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"<div class='meta'>Tempo: {Encode(value)} bpm</div>");
                return true;
            case "time":
                if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"<div class='meta'>Time: {Encode(value)}</div>");
                return true;

            case "comment":
            case "c":
            case "ci":
            case "comment_italic":
                if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"<div class='comment'>{Encode(value)}</div>");
                return true;
            case "cb":
            case "comment_box":
                if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"<div class='comment'><span class='comment-box'>{Encode(value)}</span></div>");
                return true;

            case "start_of_chorus":
            case "soc":
                section = SectionType.Chorus;
                sb.AppendLine($"<div class='section chorus'>{Encode((string.IsNullOrWhiteSpace(label) ? "Chorus" : label).ToUpperInvariant())}</div>");
                return true;
            case "end_of_chorus":
            case "eoc":
                section = SectionType.None;
                return true;

            case "start_of_verse":
            case "sov":
                section = SectionType.Verse;
                sb.AppendLine($"<div class='section verse'>{Encode((string.IsNullOrWhiteSpace(label) ? "Verse" : label).ToUpperInvariant())}</div>");
                return true;
            case "end_of_verse":
            case "eov":
                section = SectionType.None;
                return true;

            case "start_of_bridge":
            case "sob":
                section = SectionType.Bridge;
                sb.AppendLine($"<div class='section bridge'>{Encode((string.IsNullOrWhiteSpace(label) ? "Bridge" : label).ToUpperInvariant())}</div>");
                return true;
            case "end_of_bridge":
            case "eob":
                section = SectionType.None;
                return true;

            case "start_of_tab":
            case "sot":
                section = SectionType.Tab;
                return true;
            case "end_of_tab":
            case "eot":
                section = SectionType.None;
                return true;

            default:
                return true;
        }
    }

    private static void RenderLyricLine(string line, SectionType section, StringBuilder sb)
    {
        if (section == SectionType.Tab)
        {
            sb.AppendLine($"<pre class='tab'>{Encode(line)}</pre>");
            return;
        }

        var sectionClass = section switch
        {
            SectionType.Chorus => " chorus",
            SectionType.Bridge => " bridge",
            _ => string.Empty
        };

        var htmlLine = ChordTokenRegex.Replace(line, m =>
        {
            var isAnnotation = m.Groups[1].Value == "*";
            var token = Encode(m.Groups[2].Value);
            return isAnnotation
                ? $"<span class='annotation'>{token}</span>"
                : $"<span class='chord'>{token}</span>";
        });

        if (string.IsNullOrWhiteSpace(htmlLine))
        {
            sb.AppendLine("<div class='line'>&nbsp;</div>");
        }
        else
        {
            sb.AppendLine($"<div class='line{sectionClass}'>{htmlLine}</div>");
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

        var colonIdx = inner.IndexOf(':');
        var spaceIdx = inner.IndexOf(' ');

        int nameEnd;
        if (colonIdx < 0 && spaceIdx < 0) nameEnd = inner.Length;
        else if (colonIdx < 0) nameEnd = spaceIdx;
        else if (spaceIdx < 0) nameEnd = colonIdx;
        else nameEnd = Math.Min(colonIdx, spaceIdx);

        name = inner[..nameEnd].Trim().ToLowerInvariant();
        var dashIdx = name.LastIndexOf('-');
        if (dashIdx > 0)
        {
            name = name[..dashIdx];
        }

        if (nameEnd < inner.Length)
        {
            value = inner[nameEnd..].TrimStart(':', ' ').Trim();
        }

        return true;
    }

    private static string ExtractLabel(string value)
    {
        var match = Regex.Match(value, "label\\s*=\\s*[\"']([^\"']*)[\"']", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return value.Contains('=') ? string.Empty : value;
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
