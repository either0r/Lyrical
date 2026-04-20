using Windows.Storage;

namespace Lyrical.Services;

public static class WhatsNewService
{
    private const string LastSeenVersionKey = "WhatsNewLastSeenVersion";

    public static string CurrentVersion
    {
        get
        {
            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public static bool ShouldShow()
    {
        var lastSeen = ApplicationData.Current.LocalSettings.Values[LastSeenVersionKey] as string ?? string.Empty;
        return !string.Equals(lastSeen, CurrentVersion, System.StringComparison.OrdinalIgnoreCase);
    }

    public static void MarkSeen()
    {
        ApplicationData.Current.LocalSettings.Values[LastSeenVersionKey] = CurrentVersion;
    }
}
