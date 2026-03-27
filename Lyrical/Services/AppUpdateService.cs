using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Lyrical.Services;

public static class AppUpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/either0r/Lyrical/releases/latest";
    private static readonly HttpClient _httpClient = new();

    static AppUpdateService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Lyrical/1.0");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static async Task<UpdateCheckResult> CheckForUpdateAsync(bool force)
    {
        if (!force && !UpdateSettingsService.NotificationsEnabled)
        {
            return UpdateCheckResult.NotChecked();
        }

        if (!force
            && UpdateSettingsService.LastCheckedUtc is DateTimeOffset last
            && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(24))
        {
            return UpdateCheckResult.NotChecked();
        }

        UpdateSettingsService.LastCheckedUtc = DateTimeOffset.UtcNow;

        try
        {
            using var response = await _httpClient.GetAsync(LatestReleaseUrl);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed($"GitHub returned {(int)response.StatusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            var release = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(stream);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName) || string.IsNullOrWhiteSpace(release.HtmlUrl))
            {
                return UpdateCheckResult.Failed("Invalid release payload");
            }

            var currentVersionText = GetCurrentVersionText();
            var latestVersionText = NormalizeVersionText(release.TagName);

            if (!TryParseComparableVersion(currentVersionText, out var currentVersion)
                || !TryParseComparableVersion(latestVersionText, out var latestVersion))
            {
                return UpdateCheckResult.Failed("Could not parse version");
            }

            var available = latestVersion > currentVersion;
            var alreadyNotified = string.Equals(UpdateSettingsService.LastNotifiedVersion, latestVersionText, StringComparison.OrdinalIgnoreCase);

            if (!force && alreadyNotified)
            {
                return UpdateCheckResult.NotChecked();
            }

            return new UpdateCheckResult(
                IsChecked: true,
                IsUpdateAvailable: available,
                CurrentVersion: currentVersionText,
                LatestVersion: latestVersionText,
                ReleaseUrl: release.HtmlUrl,
                Error: string.Empty);
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failed(ex.Message);
        }
    }

    public static void MarkVersionNotified(string version)
    {
        if (!string.IsNullOrWhiteSpace(version))
        {
            UpdateSettingsService.LastNotifiedVersion = version.Trim();
        }
    }

    private static string GetCurrentVersionText()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }

    private static string NormalizeVersionText(string tag)
    {
        var trimmed = tag.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[1..];
        }

        var dashIndex = trimmed.IndexOf('-');
        if (dashIndex > 0)
        {
            trimmed = trimmed[..dashIndex];
        }

        return trimmed;
    }

    private static bool TryParseComparableVersion(string text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (!Version.TryParse(text, out var parsed))
        {
            return false;
        }

        version = parsed.Build < 0
            ? new Version(parsed.Major, parsed.Minor, 0)
            : new Version(parsed.Major, parsed.Minor, parsed.Build);

        return true;
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}

public sealed record UpdateCheckResult(
    bool IsChecked,
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string Error)
{
    public static UpdateCheckResult NotChecked() => new(false, false, string.Empty, string.Empty, string.Empty, string.Empty);

    public static UpdateCheckResult Failed(string error) => new(true, false, string.Empty, string.Empty, string.Empty, error ?? string.Empty);
}
