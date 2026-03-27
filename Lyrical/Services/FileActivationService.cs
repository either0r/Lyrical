using Windows.Storage;
using System.Threading.Tasks;

namespace Lyrical.Services
{
    public static class FileActivationService
    {
        public static IStorageFile? ActivationFile { get; set; }

        /// <summary>
        /// Handles a file that was activated (opened by file association or drag-drop)
        /// </summary>
        public static async Task HandleFileActivationAsync(IStorageFile file)
        {
            if (file == null)
                return;

            // Store the file for the app to open
            ActivationFile = file;
        }

        /// <summary>
        /// Clears the activation file after it has been processed
        /// </summary>
        public static void ClearActivationFile()
        {
            ActivationFile = null;
        }
    }
}
