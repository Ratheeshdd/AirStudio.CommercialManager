using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Channels;
using AirStudio.CommercialManager.Core.Services.Configuration;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private AppConfiguration _config;
        private List<Channel> _channels = new List<Channel>();
        private Channel _selectedChannel;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            ChannelComboBox.SelectionChanged += ChannelComboBox_SelectionChanged;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
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

            // Load configuration
            try
            {
                _config = await ConfigurationService.Instance.LoadAsync();
                UpdateDbStatus(_config.IsValid);

                // Load channels from database
                await LoadChannelsAsync();
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load configuration", ex);
                UpdateDbStatus(false);
            }
        }

        private async System.Threading.Tasks.Task LoadChannelsAsync()
        {
            try
            {
                StatusLabel.Text = "Loading channels...";

                _channels = await ChannelService.Instance.GetChannelsAsync(forceRefresh: true);

                ChannelComboBox.Items.Clear();
                foreach (var channel in _channels)
                {
                    ChannelComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = channel.DisplayName,
                        Tag = channel,
                        IsEnabled = channel.IsUsable
                    });
                }

                if (_channels.Count > 0)
                {
                    var usableCount = _channels.FindAll(c => c.IsUsable).Count;
                    StatusLabel.Text = $"Loaded {_channels.Count} channels ({usableCount} usable)";
                    LogService.Info($"Loaded {_channels.Count} channels, {usableCount} usable");
                }
                else
                {
                    StatusLabel.Text = "No channels found. Check database connection.";
                    LogService.Warning("No channels loaded from database");
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load channels", ex);
                StatusLabel.Text = "Failed to load channels";
            }
        }

        private void ChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = ChannelComboBox.SelectedItem as ComboBoxItem;
            if (selectedItem?.Tag is Channel channel)
            {
                _selectedChannel = channel;
                OnChannelSelected(channel);
            }
            else
            {
                _selectedChannel = null;
                DisableAllActions();
            }
        }

        private void OnChannelSelected(Channel channel)
        {
            LogService.Info($"Channel selected: {channel.Name}");

            // Update UI
            WorkAreaTitle.Text = $"CHANNEL: {channel.Name.ToUpperInvariant()}";
            StatusLabel.Text = $"Channel: {channel.Name}";

            // Update X target status
            var accessibleTargets = channel.GetAccessibleTargets();
            XTargetLabel.Text = $"X Targets: {accessibleTargets.Count}/{channel.XRootTargets.Count}";

            // Enable action buttons
            EnableActionsForChannel(channel);
        }

        private void EnableActionsForChannel(Channel channel)
        {
            LibraryButton.IsEnabled = true;
            AgencyButton.IsEnabled = true;
            CapsuleButton.IsEnabled = true;
            ScheduleButton.IsEnabled = true;
            ImportTagButton.IsEnabled = true;
            ViewScheduleButton.IsEnabled = true;
        }

        private void DisableAllActions()
        {
            WorkAreaTitle.Text = "SELECT A CHANNEL TO BEGIN";
            XTargetLabel.Text = "X Targets: --";

            LibraryButton.IsEnabled = false;
            AgencyButton.IsEnabled = false;
            CapsuleButton.IsEnabled = false;
            ScheduleButton.IsEnabled = false;
            ImportTagButton.IsEnabled = false;
            ViewScheduleButton.IsEnabled = false;
        }

        private void UpdateDbStatus(bool connected)
        {
            if (connected)
            {
                DbStatusLabel.Text = "DB Connected";
                DbStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            }
            else
            {
                DbStatusLabel.Text = "DB Disconnected";
                DbStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            }
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
            LogService.Info("Configuration button clicked");

            var configWindow = new ConfigurationWindow();
            configWindow.Owner = this;
            configWindow.ShowDialog();

            // Reload configuration after closing
            MainWindow_Loaded(sender, e);
        }

        #endregion
    }
}