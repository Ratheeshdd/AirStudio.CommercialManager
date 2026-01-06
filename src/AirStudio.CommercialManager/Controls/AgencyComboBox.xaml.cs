using System;
using System.Collections.Generic;
using System.Linq;
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
    /// ComboBox with typeahead search for agencies
    /// Supports inline "Add Agency" if typed name doesn't exist
    /// </summary>
    public partial class AgencyComboBox : UserControl
    {
        private Channel _channel;
        private AgencyService _agencyService;
        private List<Agency> _allAgencies = new List<Agency>();
        private bool _isUpdatingText = false;

        /// <summary>
        /// Event fired when a new agency is created via inline add
        /// </summary>
        public event EventHandler<Agency> AgencyCreated;

        /// <summary>
        /// Event fired when selection changes
        /// </summary>
        public event EventHandler<Agency> SelectionChanged;

        /// <summary>
        /// Currently selected agency
        /// </summary>
        public Agency SelectedAgency => AgencyCombo.SelectedItem as Agency;

        /// <summary>
        /// Selected agency code
        /// </summary>
        public int? SelectedAgencyCode => SelectedAgency?.Code;

        public AgencyComboBox()
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
        public async System.Threading.Tasks.Task LoadAgenciesAsync()
        {
            try
            {
                _allAgencies = await _agencyService.GetAgenciesAsync(forceRefresh: true);
                AgencyCombo.ItemsSource = _allAgencies;
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load agencies for combobox", ex);
            }
        }

        /// <summary>
        /// Set the selected agency by code
        /// </summary>
        public void SetSelectedAgencyCode(int code)
        {
            var agency = _allAgencies.FirstOrDefault(a => a.Code == code);
            if (agency != null)
            {
                AgencyCombo.SelectedItem = agency;
            }
        }

        /// <summary>
        /// Clear selection
        /// </summary>
        public void Clear()
        {
            AgencyCombo.SelectedItem = null;
            AgencyCombo.Text = string.Empty;
        }

        #region Event Handlers

        private void AgencyCombo_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Filter the list based on typed text
            var textBox = AgencyCombo.Template.FindName("PART_EditableTextBox", AgencyCombo) as TextBox;
            if (textBox != null)
            {
                var currentText = textBox.Text.Insert(textBox.SelectionStart, e.Text);
                FilterAgencies(currentText);
            }
        }

        private void FilterAgencies(string searchText)
        {
            if (_isUpdatingText) return;

            try
            {
                _isUpdatingText = true;

                if (string.IsNullOrWhiteSpace(searchText))
                {
                    AgencyCombo.ItemsSource = _allAgencies;
                }
                else
                {
                    var lowerSearch = searchText.ToLowerInvariant();
                    var filtered = _allAgencies
                        .Where(a => a.AgencyName != null &&
                                   a.AgencyName.ToLowerInvariant().Contains(lowerSearch))
                        .OrderBy(a => !a.AgencyName.ToLowerInvariant().StartsWith(lowerSearch))
                        .ThenBy(a => a.AgencyName)
                        .ToList();

                    AgencyCombo.ItemsSource = filtered;

                    if (filtered.Count > 0 && !AgencyCombo.IsDropDownOpen)
                    {
                        AgencyCombo.IsDropDownOpen = true;
                    }
                }
            }
            finally
            {
                _isUpdatingText = false;
            }
        }

        private void AgencyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingText) return;

            var selected = AgencyCombo.SelectedItem as Agency;
            SelectionChanged?.Invoke(this, selected);
        }

        private void AgencyCombo_DropDownClosed(object sender, EventArgs e)
        {
            // Check if typed text doesn't match any agency - offer to add
            if (AgencyCombo.SelectedItem == null && !string.IsNullOrWhiteSpace(AgencyCombo.Text))
            {
                var typedText = AgencyCombo.Text.Trim();
                var exactMatch = _allAgencies.FirstOrDefault(a =>
                    string.Equals(a.AgencyName, typedText, StringComparison.OrdinalIgnoreCase));

                if (exactMatch != null)
                {
                    AgencyCombo.SelectedItem = exactMatch;
                }
                else
                {
                    // Prompt to add new agency
                    PromptAddNewAgency(typedText);
                }
            }
        }

        private void AgencyCombo_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Check if we need to add a new agency
                if (AgencyCombo.SelectedItem == null && !string.IsNullOrWhiteSpace(AgencyCombo.Text))
                {
                    var typedText = AgencyCombo.Text.Trim();
                    var exactMatch = _allAgencies.FirstOrDefault(a =>
                        string.Equals(a.AgencyName, typedText, StringComparison.OrdinalIgnoreCase));

                    if (exactMatch != null)
                    {
                        AgencyCombo.SelectedItem = exactMatch;
                    }
                    else
                    {
                        PromptAddNewAgency(typedText);
                    }
                }

                e.Handled = true;
            }
        }

        private async void PromptAddNewAgency(string agencyName)
        {
            var result = MessageBox.Show(
                $"Agency '{agencyName}' not found.\n\nWould you like to add it as a new agency?",
                "Add New Agency",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var dialog = new AgencyDialog(agencyName);
                dialog.Owner = Window.GetWindow(this);

                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        var addResult = await _agencyService.AddAgencyAsync(dialog.Agency);

                        if (addResult.Success)
                        {
                            // Refresh the list and select the new agency
                            await LoadAgenciesAsync();
                            var newAgency = _allAgencies.FirstOrDefault(a =>
                                string.Equals(a.AgencyName, dialog.Agency.AgencyName, StringComparison.OrdinalIgnoreCase));

                            if (newAgency != null)
                            {
                                AgencyCombo.SelectedItem = newAgency;
                            }

                            AgencyCreated?.Invoke(this, dialog.Agency);
                        }
                        else
                        {
                            MessageBox.Show($"Failed to add agency: {addResult.ErrorMessage}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Error("Failed to add new agency", ex);
                        MessageBox.Show($"Failed to add agency: {ex.Message}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                // Clear the text since no valid selection
                AgencyCombo.Text = string.Empty;
            }
        }

        #endregion
    }
}
