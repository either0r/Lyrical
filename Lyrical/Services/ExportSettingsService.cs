using Windows.Storage;

namespace Lyrical.Services;

public static class ExportSettingsService
{
    private const string ExportHtmlOnSaveKey = "ExportHtmlOnSave";

    public static bool ExportHtmlOnSave
    {
        get
        {
            if (ApplicationData.Current.LocalSettings.Values[ExportHtmlOnSaveKey] is bool enabled)
            {
                return enabled;
            }

            return true;
        }
        set
        {
            ApplicationData.Current.LocalSettings.Values[ExportHtmlOnSaveKey] = value;
        }
    }
}
