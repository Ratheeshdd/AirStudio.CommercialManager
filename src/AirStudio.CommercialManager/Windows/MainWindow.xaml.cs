using System;
using System.Security.Principal;
using System.Windows;
using System.Windows.Input;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Display current user
            var currentUser = WindowsIdentity.GetCurrent();
            UserLabel.Text = $"User: {currentUser.Name}";

            // Check if user is a local administrator
            var principal = new WindowsPrincipal(currentUser);
            bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);

            // Show config button only for admins
            ConfigButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

            LogService.Info($"MainWindow loaded. User: {currentUser.Name}, IsAdmin: {isAdmin}");

            // TODO: Load channels from database
            // TODO: Check user authorization
        }

        #region Title Bar Handlers

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaximizeButton_Click(sender, e);
            }
            else
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "☐";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Check for unsaved changes before closing
            Close();
        }

        #endregion

        #region Navigation Handlers

        private void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Open configuration window
            LogService.Info("Configuration button clicked");
            MessageBox.Show("Configuration window will be implemented.", "Configuration",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion
    }
}