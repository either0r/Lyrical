using System;
using Windows.Storage;

namespace Lyrical.Services;

public static class UpdateSettingsService
{
    private const string NotificationsEnabledKey = "UpdateNotificationsEnabled";
    private const string LastCheckedUtcKey = "UpdateLastCheckedUtc";
    private const string LastNotifiedVersionKey = "UpdateLastNotifiedVersion";

    public static bool NotificationsEnabled
    {
        get
        {
            if (ApplicationData.Current.LocalSettings.Values[NotificationsEnabledKey] is bool enabled)
            {
                return enabled;
            }

            return true;
        }
        set
        {
            ApplicationData.Current.LocalSettings.Values[NotificationsEnabledKey] = value;
        }
    }

    public static DateTimeOffset? LastCheckedUtc
    {
        get
        {
            if (ApplicationData.Current.LocalSettings.Values[LastCheckedUtcKey] is string raw
                && DateTimeOffset.TryParse(raw, out var parsed))
            {
                return parsed;
            }

            return null;
        }
        set
        {
            if (value is null)
            {
                ApplicationData.Current.LocalSettings.Values.Remove(LastCheckedUtcKey);
                return;
            }

            ApplicationData.Current.LocalSettings.Values[LastCheckedUtcKey] = value.Value.UtcDateTime.ToString("O");
        }
    }

    public static string LastNotifiedVersion
    {
        get
        {
            return ApplicationData.Current.LocalSettings.Values[LastNotifiedVersionKey] as string ?? string.Empty;
        }
        set
        {
            ApplicationData.Current.LocalSettings.Values[LastNotifiedVersionKey] = value ?? string.Empty;
        }
    }
}
