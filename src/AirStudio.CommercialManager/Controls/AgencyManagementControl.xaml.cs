using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Agencies;
using AirStudio.CommercialManager.Core.Services.Logging;
using AirStudio.CommercialManager.Windows;

namespace AirStudio.CommercialManager.Controls
{
    /// <summary>
    /// Control for managing agencies per channel
    /// </summary>
    public partial class AgencyManagementControl : UserControl
    {
        private Channel _channel;
        private AgencyService _agencyService;
        private List<Agency> _agencies = new List<Agency>();

        public event EventHandler<Agency> AgencySelected;

        public AgencyManagementControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialize with a channel
        /// </summary>
        public async void Initialize(Channel channel)
        {
            _channel = channel;
            _agencyService = new AgencyService(channel);
            await LoadAgenciesAsync();
        }

        /// <summary>
        /// Load agencies from database
        /// </summary>
        private async System.Threading.Tasks.Task LoadAgenciesAsync()
        {
            try
            {
                IsEnabled = false;
                _agencies = await _agencyService.GetAgenciesAsync(forceRefresh: true);
                AgencyGrid.ItemsSource = _agencies;
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load agencies", ex);
                MessageBox.Show($"Failed to load agencies: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        /// <summary>
        /// Search agencies by text
        /// </summary>
        private async System.Threading.Tasks.Task SearchAgenciesAsync(string searchText)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(searchText))
                {
                    AgencyGrid.ItemsSource = _agencies;
                }
                else
                {
                    var filtered = await _agencyService.SearchAgenciesAsync(searchText);
                    AgencyGrid.ItemsSource = filtered;
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to search agencies", ex);
            }
        }

        #region Event Handlers

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _ = SearchAgenciesAsync(SearchTextBox.Text);
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadAgenciesAsync();
            SearchTextBox.Clear();
        }

        private void AgencyGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = AgencyGrid.SelectedItem as Agency;
            EditButton.IsEnabled = selected != null;
            DeleteButton.IsEnabled = selected != null;

            AgencySelected?.Invoke(this, selected);
        }

        private void AgencyGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            EditButton_Click(sender, e);
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AgencyDialog();
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    IsEnabled = false;
                    var result = await _agencyService.AddAgencyAsync(dialog.Agency);

                    if (result.Success)
                    {
                        await LoadAgenciesAsync();
                        MessageBox.Show($"Agency '{dialog.Agency.AgencyName}' added successfully.",
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show($"Failed to add agency: {result.ErrorMessage}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                finally
                {
                    IsEnabled = true;
                }
            }
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = AgencyGrid.SelectedItem as Agency;
            if (selected == null)
            {
                MessageBox.Show("Please select an agency to edit.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new AgencyDialog(selected.Clone());
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    IsEnabled = false;
                    var result = await _agencyService.UpdateAgencyAsync(dialog.Agency);

                    if (result.Success)
                    {
                        await LoadAgenciesAsync();
                        MessageBox.Show($"Agency '{dialog.Agency.AgencyName}' updated successfully.",
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show($"Failed to update agency: {result.ErrorMessage}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                finally
                {
                    IsEnabled = true;
                }
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = AgencyGrid.SelectedItem as Agency;
            if (selected == null)
            {
                MessageBox.Show("Please select an agency to delete.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Are you sure you want to delete agency '{selected.AgencyName}'?\n\nThis action cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                IsEnabled = false;
                var result = await _agencyService.DeleteAgencyAsync(selected.Code);

                if (result.Success)
                {
                    await LoadAgenciesAsync();
                    MessageBox.Show($"Agency '{selected.AgencyName}' deleted successfully.",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to delete agency: {result.ErrorMessage}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                IsEnabled = true;
            }
        }

        #endregion
    }
}
