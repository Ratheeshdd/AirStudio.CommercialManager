using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Agencies;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Windows
{
    /// <summary>
    /// Window for managing agencies - Add, Edit, Delete
    /// </summary>
    public partial class AgencyManagementWindow : Window
    {
        private readonly Channel _channel;
        private readonly AgencyService _agencyService;
        private List<Agency> _allAgencies = new List<Agency>();
        private Agency _selectedAgency;

        public AgencyManagementWindow(Channel channel)
        {
            InitializeComponent();
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _agencyService = new AgencyService(channel);
            Title = $"Agency Management - {channel.Name}";
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadAgenciesAsync();
        }

        private async System.Threading.Tasks.Task LoadAgenciesAsync()
        {
            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                _allAgencies = await _agencyService.LoadAgenciesAsync();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load agencies", ex);
                MessageBox.Show($"Failed to load agencies: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyFilter()
        {
            var searchText = SearchTextBox.Text?.Trim().ToLowerInvariant() ?? "";

            IEnumerable<Agency> filtered = _allAgencies;

            if (!string.IsNullOrEmpty(searchText))
            {
                filtered = _allAgencies.Where(a =>
                    (a.AgencyName?.ToLowerInvariant().Contains(searchText) ?? false) ||
                    (a.Address?.ToLowerInvariant().Contains(searchText) ?? false) ||
                    (a.Phone?.Contains(searchText) ?? false) ||
                    (a.Email?.ToLowerInvariant().Contains(searchText) ?? false) ||
                    a.Code.ToString().Contains(searchText));
            }

            AgenciesDataGrid.ItemsSource = filtered.ToList();
            UpdateSelectionInfo();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            ApplyFilter();
        }

        private void AgenciesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedAgency = AgenciesDataGrid.SelectedItem as Agency;
            UpdateSelectionInfo();
        }

        private void UpdateSelectionInfo()
        {
            var hasSelection = _selectedAgency != null;
            EditButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;

            if (hasSelection)
            {
                SelectionInfoText.Text = $"Selected: {_selectedAgency.AgencyName} (Code: {_selectedAgency.Code})";
            }
            else
            {
                SelectionInfoText.Text = "Select an agency to edit or delete";
            }
        }

        private void AgenciesDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_selectedAgency != null)
            {
                EditAgency();
            }
        }

        private void AddAgencyButton_Click(object sender, RoutedEventArgs e)
        {
            AddAgency();
        }

        private void EditAgencyButton_Click(object sender, RoutedEventArgs e)
        {
            EditAgency();
        }

        private void EditAgencyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            EditAgency();
        }

        private void DeleteAgencyButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteAgency();
        }

        private void DeleteAgencyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            DeleteAgency();
        }

        private async void AddAgency()
        {
            var dialog = new AgencyDialog();
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var result = await _agencyService.AddAgencyAsync(dialog.Agency);

                    if (result.Success)
                    {
                        LogService.Info($"Added agency: {dialog.Agency.AgencyName}");
                        await LoadAgenciesAsync();

                        // Select the new agency
                        var newAgency = _allAgencies.FirstOrDefault(a => a.Code == dialog.Agency.Code);
                        if (newAgency != null)
                        {
                            AgenciesDataGrid.SelectedItem = newAgency;
                            AgenciesDataGrid.ScrollIntoView(newAgency);
                        }
                    }
                    else
                    {
                        MessageBox.Show($"Failed to add agency: {result.ErrorMessage}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error("Failed to add agency", ex);
                    MessageBox.Show($"Failed to add agency: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void EditAgency()
        {
            if (_selectedAgency == null)
            {
                MessageBox.Show("Please select an agency to edit.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Create a copy for editing
            var agencyCopy = new Agency
            {
                Code = _selectedAgency.Code,
                AgencyName = _selectedAgency.AgencyName,
                Address = _selectedAgency.Address,
                PIN = _selectedAgency.PIN,
                Phone = _selectedAgency.Phone,
                Email = _selectedAgency.Email
            };

            var dialog = new AgencyDialog(agencyCopy);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var result = await _agencyService.UpdateAgencyAsync(dialog.Agency);

                    if (result.Success)
                    {
                        LogService.Info($"Updated agency: {dialog.Agency.AgencyName}");
                        await LoadAgenciesAsync();

                        // Re-select the edited agency
                        var updatedAgency = _allAgencies.FirstOrDefault(a => a.Code == dialog.Agency.Code);
                        if (updatedAgency != null)
                        {
                            AgenciesDataGrid.SelectedItem = updatedAgency;
                            AgenciesDataGrid.ScrollIntoView(updatedAgency);
                        }
                    }
                    else
                    {
                        MessageBox.Show($"Failed to update agency: {result.ErrorMessage}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error("Failed to update agency", ex);
                    MessageBox.Show($"Failed to update agency: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void DeleteAgency()
        {
            if (_selectedAgency == null)
            {
                MessageBox.Show("Please select an agency to delete.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Check if agency has commercials
            var isReferenced = await _agencyService.IsAgencyReferencedAsync(_selectedAgency.Code);
            if (isReferenced)
            {
                MessageBox.Show(
                    $"Cannot delete agency '{_selectedAgency.AgencyName}' because it has active commercials.\n\n" +
                    "To delete this agency, first remove or reassign all commercials associated with it.",
                    "Cannot Delete",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Confirm deletion
            var confirmResult = MessageBox.Show(
                $"Are you sure you want to delete agency '{_selectedAgency.AgencyName}'?\n\n" +
                "This action cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes)
                return;

            try
            {
                var result = await _agencyService.DeleteAgencyAsync(_selectedAgency.Code);

                if (result.Success)
                {
                    LogService.Info($"Deleted agency: {_selectedAgency.AgencyName}");
                    await LoadAgenciesAsync();
                }
                else
                {
                    MessageBox.Show($"Failed to delete agency: {result.ErrorMessage}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to delete agency", ex);
                MessageBox.Show($"Failed to delete agency: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
