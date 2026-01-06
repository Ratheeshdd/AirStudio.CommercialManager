using System;
using System.Threading.Tasks;
using System.Windows;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Configuration;
using AirStudio.CommercialManager.Core.Services.Logging;
using AirStudio.CommercialManager.Core.Services.Security;

namespace AirStudio.CommercialManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            // Initialize logging
            LogService.Initialize();
            LogService.Info("AirStudio Commercial Manager starting...");

            // Load configuration
            AppConfiguration config;
            try
            {
                config = await ConfigurationService.Instance.LoadAsync();
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load configuration", ex);
                config = null;
            }

            // Check user authorization
            var authResult = SecurityService.CheckAuthorization(config);

            if (!authResult.IsAuthorized)
            {
                LogService.Warning($"Access denied for user: {authResult.Username}");

                MessageBox.Show(
                    authResult.Message,
                    "Access Denied - AirStudio Commercial Manager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Stop);

                Shutdown(1);
                return;
            }

            // Log successful authorization
            if (authResult.IsInitialSetup)
            {
                LogService.Info($"Initial setup mode - Admin '{authResult.Username}' granted access to configure application");
            }

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
