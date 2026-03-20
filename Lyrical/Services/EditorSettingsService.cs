using Lyrical.Models;
using Windows.Storage;

namespace Lyrical.Services;

public static class EditorSettingsService
{
    private const string AutoSaveModeKey = "EditorAutoSaveMode";
    private const string AutoSaveDelaySecondsKey = "EditorAutoSaveDelaySeconds";

    private const int DefaultDelaySeconds = 5;
    private const int MinDelaySeconds = 1;
    private const int MaxDelaySeconds = 120;

    public static AutoSaveMode AutoSaveMode
    {
        get
        {
            if (ApplicationData.Current.LocalSettings.Values[AutoSaveModeKey] is string saved
                && System.Enum.TryParse<AutoSaveMode>(saved, out var mode))
            {
                return mode;
            }

            return Models.AutoSaveMode.Off;
        }
        set
        {
            ApplicationData.Current.LocalSettings.Values[AutoSaveModeKey] = value.ToString();
        }
    }

    public static int AutoSaveDelaySeconds
    {
        get
        {
            if (ApplicationData.Current.LocalSettings.Values[AutoSaveDelaySecondsKey] is int stored)
            {
                return ClampDelay(stored);
            }

            return DefaultDelaySeconds;
        }
        set
        {
            ApplicationData.Current.LocalSettings.Values[AutoSaveDelaySecondsKey] = ClampDelay(value);
        }
    }

    private static int ClampDelay(int value)
    {
        if (value < MinDelaySeconds)
        {
            return MinDelaySeconds;
        }

        if (value > MaxDelaySeconds)
        {
            return MaxDelaySeconds;
        }

        return value;
    }
}
