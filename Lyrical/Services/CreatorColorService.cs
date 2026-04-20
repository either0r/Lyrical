using System;
using Windows.Storage;
using Windows.UI;

namespace Lyrical.Services;

public static class CreatorColorService
{
    private const string CurrentUserColorOverrideKey = "CurrentUserSongColorOverride";

    private static readonly Color[] Palette =
    [
        Color.FromArgb(255, 52, 101, 164),
        Color.FromArgb(255, 94, 53, 177),
        Color.FromArgb(255, 0, 121, 107),
        Color.FromArgb(255, 2, 136, 209),
        Color.FromArgb(255, 121, 85, 72),
        Color.FromArgb(255, 85, 139, 47),
        Color.FromArgb(255, 194, 24, 91),
        Color.FromArgb(255, 0, 105, 92),
        Color.FromArgb(255, 49, 27, 146),
        Color.FromArgb(255, 21, 101, 192)
    ];

    private static readonly Color FallbackColor = Color.FromArgb(255, 45, 45, 45);
    private static readonly Color LightTextColor = Color.FromArgb(255, 255, 255, 255);
    private static readonly Color DarkTextColor = Color.FromArgb(255, 0, 0, 0);

    public static Color ResolveColor(string? creator)
    {
        var normalizedCreator = NormalizeCreator(creator);
        if (string.IsNullOrWhiteSpace(normalizedCreator))
        {
            return FallbackColor;
        }

        if (IsCurrentUser(normalizedCreator) && TryGetCurrentUserOverrideColor(out var overrideColor))
        {
            return overrideColor;
        }

        var hash = ComputeDeterministicHash(normalizedCreator);
        var paletteIndex = (int)(hash % (uint)Palette.Length);
        return Palette[paletteIndex];
    }

    public static bool TryGetCurrentUserOverrideColor(out Color color)
    {
        if (ApplicationData.Current.LocalSettings.Values[CurrentUserColorOverrideKey] is string raw
            && TryParseHexColor(raw, out color))
        {
            return true;
        }

        color = default;
        return false;
    }

    public static void SetCurrentUserOverrideColor(Color color)
    {
        ApplicationData.Current.LocalSettings.Values[CurrentUserColorOverrideKey] = ToHexColor(color);
    }

    public static void ClearCurrentUserOverrideColor()
    {
        ApplicationData.Current.LocalSettings.Values.Remove(CurrentUserColorOverrideKey);
    }

    public static string GetCurrentUserNameOrUnknown()
    {
        return string.IsNullOrWhiteSpace(Environment.UserName)
            ? "Unknown"
            : Environment.UserName.Trim();
    }

    public static Color ResolveTextColor(string? creator)
    {
        var background = ResolveColor(creator);

        var lightContrast = GetContrastRatio(background, LightTextColor);
        var darkContrast = GetContrastRatio(background, DarkTextColor);

        return lightContrast >= darkContrast ? LightTextColor : DarkTextColor;
    }

    private static bool IsCurrentUser(string creator)
    {
        return string.Equals(creator, GetCurrentUserNameOrUnknown(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCreator(string? creator)
    {
        return creator?.Trim() ?? string.Empty;
    }

    private static uint ComputeDeterministicHash(string input)
    {
        const uint fnvPrime = 16777619;
        uint hash = 2166136261;

        foreach (var ch in input)
        {
            hash ^= char.ToUpperInvariant(ch);
            hash *= fnvPrime;
        }

        return hash;
    }

    private static bool TryParseHexColor(string hex, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var value = hex.Trim().TrimStart('#');
        if (value.Length == 6)
        {
            value = "FF" + value;
        }

        if (value.Length != 8)
        {
            return false;
        }

        if (!byte.TryParse(value[..2], System.Globalization.NumberStyles.HexNumber, null, out var a)
            || !byte.TryParse(value.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
            || !byte.TryParse(value.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
            || !byte.TryParse(value.Substring(6, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = Color.FromArgb(a, r, g, b);
        return true;
    }

    private static double GetContrastRatio(Color first, Color second)
    {
        var firstLuminance = GetRelativeLuminance(first);
        var secondLuminance = GetRelativeLuminance(second);

        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double GetRelativeLuminance(Color color)
    {
        var r = ToLinear(color.R / 255.0);
        var g = ToLinear(color.G / 255.0);
        var b = ToLinear(color.B / 255.0);

        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }

    private static double ToLinear(double channel)
    {
        return channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static string ToHexColor(Color color)
    {
        return $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
