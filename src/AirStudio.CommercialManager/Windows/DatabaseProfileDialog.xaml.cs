using System;
using System.Windows;
using AirStudio.CommercialManager.Core.Models;

namespace AirStudio.CommercialManager.Windows
{
    /// <summary>
    /// Dialog for adding/editing a database profile
    /// </summary>
    public partial class DatabaseProfileDialog : Window
    {
        public DatabaseProfile Profile { get; private set; }

        public DatabaseProfileDialog()
        {
            InitializeComponent();
            Profile = new DatabaseProfile();
        }

        public DatabaseProfileDialog(DatabaseProfile existingProfile) : this()
        {
            Profile = existingProfile;

            // Populate fields
            NameTextBox.Text = existingProfile.Name;
            HostTextBox.Text = existingProfile.Host;
            PortTextBox.Text = existingProfile.Port.ToString();
            UsernameTextBox.Text = existingProfile.Username;
            PasswordBox.Password = existingProfile.Password;
            TimeoutTextBox.Text = existingProfile.TimeoutSeconds.ToString();

            // Set SSL mode
            for (int i = 0; i < SslModeComboBox.Items.Count; i++)
            {
                var item = SslModeComboBox.Items[i] as System.Windows.Controls.ComboBoxItem;
                if (item != null && item.Content.ToString() == existingProfile.SslMode)
                {
                    SslModeComboBox.SelectedIndex = i;
                    break;
                }
            }
        }

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput())
                return;

            try
            {
                IsEnabled = false;
                var profile = BuildProfile();
                var connectionString = profile.BuildConnectionString(profile.Password, "air_virtual_studio");

                using (var connection = new MySqlConnector.MySqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    MessageBox.Show("Connection successful!",
                        "Test Connection", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection failed: {ex.Message}",
                    "Test Connection", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput())
                return;

            Profile = BuildProfile();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(NameTextBox.Text))
            {
                MessageBox.Show("Please enter a profile name.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                NameTextBox.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(HostTextBox.Text))
            {
                MessageBox.Show("Please enter a host/IP.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                HostTextBox.Focus();
                return false;
            }

            if (!int.TryParse(PortTextBox.Text, out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("Please enter a valid port (1-65535).",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                PortTextBox.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
            {
                MessageBox.Show("Please enter a username.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                UsernameTextBox.Focus();
                return false;
            }

            return true;
        }

        private DatabaseProfile BuildProfile()
        {
            var sslModeItem = SslModeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;

            return new DatabaseProfile
            {
                Id = Profile?.Id ?? Guid.NewGuid().ToString(),
                Name = NameTextBox.Text.Trim(),
                Host = HostTextBox.Text.Trim(),
                Port = int.Parse(PortTextBox.Text),
                Username = UsernameTextBox.Text.Trim(),
                Password = PasswordBox.Password,
                SslMode = sslModeItem?.Content?.ToString() ?? "Preferred",
                TimeoutSeconds = int.TryParse(TimeoutTextBox.Text, out int t) ? t : 30,
                IsDefault = Profile?.IsDefault ?? false,
                Order = Profile?.Order ?? 0
            };
        }
    }
}
