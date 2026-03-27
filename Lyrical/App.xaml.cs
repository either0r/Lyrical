using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Lyrical
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        public static Window? MainAppWindow { get; private set; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            Lyrical.Services.CustomChordService.Load();
            Lyrical.Services.ThemeService.Load();

            _window = new MainWindow();
            MainAppWindow = _window;
            _window.Activate();
            
            // Handle command-line arguments
            HandleCommandLineArgs(args.Arguments);
        }

        private void HandleCommandLineArgs(string args)
        {
            // Parse command-line arguments for file path
            if (string.IsNullOrWhiteSpace(args))
                return;

            var filePath = args.Trim();
            if (File.Exists(filePath) && (filePath.EndsWith(".cho", StringComparison.OrdinalIgnoreCase)))
            {
                // Open the file asynchronously
                _ = OpenFileFromCommandLineAsync(filePath);
            }
        }

        private async System.Threading.Tasks.Task OpenFileFromCommandLineAsync(string filePath)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                await Lyrical.Services.FileActivationService.HandleFileActivationAsync(file);
                if (_window is MainWindow mainWin)
                {
                    mainWin.OpenActivationFile();
                }
            }
            catch
            {
                // File not found or inaccessible - continue with normal startup
            }
        }
    }
}
