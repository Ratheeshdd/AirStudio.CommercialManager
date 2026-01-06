using System;
using System.Windows;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Initialize logging
            LogService.Initialize();
            LogService.Info("AirStudio Commercial Manager starting...");

            // TODO: Check user authorization before showing main window
            // TODO: Load configuration from ProgramData

            var mainWindow = new Windows.MainWindow();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            LogService.Info("AirStudio Commercial Manager shutting down.");
            LogService.Shutdown();
            base.OnExit(e);
        }
    }
}
