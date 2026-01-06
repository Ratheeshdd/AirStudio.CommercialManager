using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Security.Principal;
using System.Windows;
using Microsoft.Win32;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Configuration;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Windows
{
    /// <summary>
    /// Configuration window for admin settings
    /// </summary>
    public partial class ConfigurationWindow : Window
    {
        private ObservableCollection<DatabaseProfileViewModel> _databaseProfiles;
        private ObservableCollection<ChannelLocationViewModel> _channelLocations;
        private ObservableCollection<string> _allowedUsers;

        private bool _hasChanges = false;

        public ConfigurationWindow()
        {
            InitializeComponent();
            Loaded += ConfigurationWindow_Loaded;
            Closing += ConfigurationWindow_Closing;
        }

        private async void ConfigurationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = await ConfigurationService.Instance.LoadAsync();

                // Load database profiles
                _databaseProfiles = new ObservableCollection<DatabaseProfileViewModel>(
                    config.DatabaseProfiles?.Select(p => new DatabaseProfileViewModel(p)) ??
                    Enumerable.Empty<DatabaseProfileViewModel>());
                DatabasesGrid.ItemsSource = _databaseProfiles;

                // Load channel locations (with placeholders for channels from DB)
                _channelLocations = new ObservableCollection<ChannelLocationViewModel>(
                    config.ChannelLocations?.Select(c => new ChannelLocationViewModel(c)) ??
                    Enumerable.Empty<ChannelLocationViewModel>());
                LocationsGrid.ItemsSource = _channelLocations;

                // Load allowed users
                _allowedUsers = new ObservableCollection<string>(
                    config.AllowedUsers ?? new List<string>());
                UsersListBox.ItemsSource = _allowedUsers;

                // Load advanced settings
                SequenceBaseNNTextBox.Text = config.SequenceBaseNN.ToString();

                LogService.Info("Configuration window loaded");
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load configuration", ex);
                MessageBox.Show($"Failed to load configuration: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ConfigurationWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_hasChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Do you want to save before closing?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    SaveChanges();
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
            }
        }

        #region Database Tab Handlers

        private void AddDatabase_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new DatabaseProfileDialog();
            if (dialog.ShowDialog() == true)
            {
                _databaseProfiles.Add(new DatabaseProfileViewModel(dialog.Profile));
                _hasChanges = true;
            }
        }

        private void EditDatabase_Click(object sender, RoutedEventArgs e)
        {
            var selected = DatabasesGrid.SelectedItem as DatabaseProfileViewModel;
            if (selected == null)
            {
                MessageBox.Show("Please select a database profile to edit.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new DatabaseProfileDialog(selected.ToModel());
            if (dialog.ShowDialog() == true)
            {
                selected.UpdateFrom(dialog.Profile);
                _hasChanges = true;
            }
        }

        private void RemoveDatabase_Click(object sender, RoutedEventArgs e)
        {
            var selected = DatabasesGrid.SelectedItem as DatabaseProfileViewModel;
            if (selected == null)
            {
                MessageBox.Show("Please select a database profile to remove.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show($"Remove database profile '{selected.Name}'?",
                "Confirm Remove", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _databaseProfiles.Remove(selected);
                _hasChanges = true;
            }
        }

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            var selected = DatabasesGrid.SelectedItem as DatabaseProfileViewModel;
            if (selected == null)
            {
                MessageBox.Show("Please select a database profile to test.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IsEnabled = false;
                var profile = selected.ToModel();
                var connectionString = profile.BuildConnectionString(profile.Password, "air_virtual_studio");

                using (var connection = new MySqlConnector.MySqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    MessageBox.Show($"Connection to '{selected.Name}' successful!",
                        "Connection Test", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection failed: {ex.Message}",
                    "Connection Test", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private void SetDefault_Click(object sender, RoutedEventArgs e)
        {
            var selected = DatabasesGrid.SelectedItem as DatabaseProfileViewModel;
            if (selected == null)
            {
                MessageBox.Show("Please select a database profile to set as default.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var profile in _databaseProfiles)
            {
                profile.IsDefault = (profile == selected);
            }

            DatabasesGrid.Items.Refresh();
            _hasChanges = true;
        }

        #endregion

        #region Locations Tab Handlers

        private void EditTargets_Click(object sender, RoutedEventArgs e)
        {
            var selected = LocationsGrid.SelectedItem as ChannelLocationViewModel;
            if (selected == null)
            {
                MessageBox.Show("Please select a channel to edit targets.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var input = Microsoft.VisualBasic.Interaction.InputBox(
                $"Enter X root targets for '{selected.Channel}' (comma-separated, e.g., X:\\,Y:\\):",
                "Edit X Targets",
                string.Join(",", selected.XRootTargets ?? new List<string>()));

            if (!string.IsNullOrEmpty(input))
            {
                selected.XRootTargets = input.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
                LocationsGrid.Items.Refresh();
                _hasChanges = true;
            }
        }

        private void AddTarget_Click(object sender, RoutedEventArgs e)
        {
            var selected = LocationsGrid.SelectedItem as ChannelLocationViewModel;
            if (selected == null)
            {
                MessageBox.Show("Please select a channel first.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = $"Select X root target for {selected.Channel}"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (selected.XRootTargets == null)
                    selected.XRootTargets = new List<string>();

                if (!selected.XRootTargets.Contains(dialog.SelectedPath))
                {
                    selected.XRootTargets.Add(dialog.SelectedPath);
                    LocationsGrid.Items.Refresh();
                    _hasChanges = true;
                }
            }
        }

        private void RemoveTarget_Click(object sender, RoutedEventArgs e)
        {
            var selected = LocationsGrid.SelectedItem as ChannelLocationViewModel;
            if (selected == null || selected.XRootTargets == null || selected.XRootTargets.Count == 0)
            {
                MessageBox.Show("Please select a channel with targets to remove.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Remove the last target for simplicity
            selected.XRootTargets.RemoveAt(selected.XRootTargets.Count - 1);
            LocationsGrid.Items.Refresh();
            _hasChanges = true;
        }

        private void TestTargets_Click(object sender, RoutedEventArgs e)
        {
            var selected = LocationsGrid.SelectedItem as ChannelLocationViewModel;
            if (selected == null || selected.XRootTargets == null)
            {
                MessageBox.Show("Please select a channel to test targets.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var results = new List<string>();
            foreach (var target in selected.XRootTargets)
            {
                bool exists = System.IO.Directory.Exists(target);
                bool writable = false;

                if (exists)
                {
                    try
                    {
                        var testFile = System.IO.Path.Combine(target, ".writetest");
                        System.IO.File.WriteAllText(testFile, "test");
                        System.IO.File.Delete(testFile);
                        writable = true;
                    }
                    catch { }
                }

                results.Add($"{target}: {(exists ? "Exists" : "NOT FOUND")}, {(writable ? "Writable" : "NOT WRITABLE")}");
            }

            MessageBox.Show(string.Join("\n", results), "Target Test Results",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RefreshChannels_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Refresh channels from database
            MessageBox.Show("Channel refresh will be implemented with the channel loader.",
                "Not Implemented", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region Users Tab Handlers

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            var input = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter username (DOMAIN\\user or MACHINE\\user):",
                "Add User");

            if (!string.IsNullOrWhiteSpace(input))
            {
                if (!_allowedUsers.Contains(input, StringComparer.OrdinalIgnoreCase))
                {
                    _allowedUsers.Add(input);
                    _hasChanges = true;
                }
            }
        }

        private void RemoveUser_Click(object sender, RoutedEventArgs e)
        {
            var selected = UsersListBox.SelectedItem as string;
            if (selected == null)
            {
                MessageBox.Show("Please select a user to remove.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _allowedUsers.Remove(selected);
            _hasChanges = true;
        }

        private void AddCurrentUser_Click(object sender, RoutedEventArgs e)
        {
            var currentUser = WindowsIdentity.GetCurrent().Name;
            if (!_allowedUsers.Contains(currentUser, StringComparer.OrdinalIgnoreCase))
            {
                _allowedUsers.Add(currentUser);
                _hasChanges = true;
            }
        }

        #endregion

        #region Advanced Tab Handlers

        private async void ExportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = $"CommercialManager_Config_{DateTime.Now:yyyyMMdd}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Save current changes first
                    SaveChanges();

                    await ConfigurationService.Instance.ExportAsync(dialog.FileName);
                    MessageBox.Show("Configuration exported successfully.",
                        "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}",
                        "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Importing settings will overwrite all current configuration. A backup will be created. Continue?",
                "Confirm Import",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            var dialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    await ConfigurationService.Instance.ImportAsync(dialog.FileName);
                    MessageBox.Show("Configuration imported successfully. The window will reload.",
                        "Import", MessageBoxButton.OK, MessageBoxImage.Information);

                    _hasChanges = false;
                    ConfigurationWindow_Loaded(sender, e);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Import failed: {ex.Message}",
                        "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Footer Handlers

        private async void SaveChanges_Click(object sender, RoutedEventArgs e)
        {
            SaveChanges();
            MessageBox.Show("Configuration saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _hasChanges = false;
            Close();
        }

        private async void SaveChanges()
        {
            try
            {
                var config = ConfigurationService.Instance.CurrentConfig;

                // Update database profiles
                config.DatabaseProfiles = _databaseProfiles.Select(p => p.ToModel()).ToList();

                // Update channel locations
                config.ChannelLocations = _channelLocations.Select(c => c.ToModel()).ToList();

                // Update allowed users
                config.AllowedUsers = _allowedUsers.ToList();

                // Update sequence base NN
                if (int.TryParse(SequenceBaseNNTextBox.Text, out int nn))
                {
                    config.SequenceBaseNN = nn;
                }

                await ConfigurationService.Instance.SaveAsync();
                _hasChanges = false;

                LogService.Info("Configuration saved");
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to save configuration", ex);
                MessageBox.Show($"Failed to save configuration: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }

    #region View Models

    public class DatabaseProfileViewModel : INotifyPropertyChanged
    {
        private string _id;
        private string _name;
        private string _host;
        private int _port;
        private string _username;
        private string _password;
        private string _sslMode;
        private int _timeoutSeconds;
        private bool _isDefault;
        private int _order;

        public string Id { get => _id; set { _id = value; OnPropertyChanged(nameof(Id)); } }
        public string Name { get => _name; set { _name = value; OnPropertyChanged(nameof(Name)); } }
        public string Host { get => _host; set { _host = value; OnPropertyChanged(nameof(Host)); } }
        public int Port { get => _port; set { _port = value; OnPropertyChanged(nameof(Port)); } }
        public string Username { get => _username; set { _username = value; OnPropertyChanged(nameof(Username)); } }
        public string Password { get => _password; set { _password = value; OnPropertyChanged(nameof(Password)); } }
        public string SslMode { get => _sslMode; set { _sslMode = value; OnPropertyChanged(nameof(SslMode)); } }
        public int TimeoutSeconds { get => _timeoutSeconds; set { _timeoutSeconds = value; OnPropertyChanged(nameof(TimeoutSeconds)); } }
        public bool IsDefault { get => _isDefault; set { _isDefault = value; OnPropertyChanged(nameof(IsDefault)); } }
        public int Order { get => _order; set { _order = value; OnPropertyChanged(nameof(Order)); } }

        public DatabaseProfileViewModel() { }

        public DatabaseProfileViewModel(DatabaseProfile model)
        {
            Id = model.Id;
            Name = model.Name;
            Host = model.Host;
            Port = model.Port;
            Username = model.Username;
            Password = model.Password;
            SslMode = model.SslMode;
            TimeoutSeconds = model.TimeoutSeconds;
            IsDefault = model.IsDefault;
            Order = model.Order;
        }

        public void UpdateFrom(DatabaseProfile model)
        {
            Name = model.Name;
            Host = model.Host;
            Port = model.Port;
            Username = model.Username;
            Password = model.Password;
            SslMode = model.SslMode;
            TimeoutSeconds = model.TimeoutSeconds;
        }

        public DatabaseProfile ToModel()
        {
            return new DatabaseProfile
            {
                Id = Id ?? Guid.NewGuid().ToString(),
                Name = Name,
                Host = Host,
                Port = Port,
                Username = Username,
                Password = Password,
                SslMode = SslMode,
                TimeoutSeconds = TimeoutSeconds,
                IsDefault = IsDefault,
                Order = Order
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ChannelLocationViewModel : INotifyPropertyChanged
    {
        private string _channel;
        private List<string> _xRootTargets;

        public string Channel { get => _channel; set { _channel = value; OnPropertyChanged(nameof(Channel)); } }
        public List<string> XRootTargets
        {
            get => _xRootTargets;
            set
            {
                _xRootTargets = value;
                OnPropertyChanged(nameof(XRootTargets));
                OnPropertyChanged(nameof(XRootTargetsDisplay));
                OnPropertyChanged(nameof(IsConfigured));
            }
        }

        public string XRootTargetsDisplay => XRootTargets != null ? string.Join(", ", XRootTargets) : "";
        public bool IsConfigured => XRootTargets != null && XRootTargets.Count > 0;

        public ChannelLocationViewModel() { }

        public ChannelLocationViewModel(ChannelLocation model)
        {
            Channel = model.Channel;
            XRootTargets = model.XRootTargets?.ToList() ?? new List<string>();
        }

        public ChannelLocation ToModel()
        {
            return new ChannelLocation
            {
                Channel = Channel,
                XRootTargets = XRootTargets?.ToList() ?? new List<string>()
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    #endregion
}
