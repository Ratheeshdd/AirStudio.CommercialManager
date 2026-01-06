using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Audio;
using AirStudio.CommercialManager.Core.Services.Library;
using AirStudio.CommercialManager.Core.Services.Logging;
using AirStudio.CommercialManager.Windows;

namespace AirStudio.CommercialManager.Controls
{
    /// <summary>
    /// Control for managing commercial library per channel
    /// </summary>
    public partial class LibraryControl : UserControl
    {
        private Channel _channel;
        private CommercialService _commercialService;
        private AudioService _audioService = new AudioService();
        private List<Commercial> _commercials = new List<Commercial>();

        public event EventHandler<Commercial> CommercialSelected;
        public event EventHandler<Commercial> ScheduleRequested;

        public LibraryControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialize with a channel
        /// </summary>
        public async void Initialize(Channel channel)
        {
            _channel = channel;
            _commercialService = new CommercialService(channel);
            await LoadCommercialsAsync();
        }

        /// <summary>
        /// Load commercials from database
        /// </summary>
        private async System.Threading.Tasks.Task LoadCommercialsAsync()
        {
            try
            {
                IsEnabled = false;
                _commercials = await _commercialService.GetCommercialsAsync(forceRefresh: true);
                CommercialGrid.ItemsSource = _commercials;
                UpdateItemCount();
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load commercials", ex);
                MessageBox.Show($"Failed to load commercials: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private void UpdateItemCount()
        {
            var displayed = CommercialGrid.ItemsSource as IEnumerable<Commercial>;
            ItemCountLabel.Text = $"{displayed?.Count() ?? 0} items";
        }

        /// <summary>
        /// Search commercials
        /// </summary>
        private async System.Threading.Tasks.Task SearchCommercialsAsync(string searchText)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(searchText))
                {
                    CommercialGrid.ItemsSource = _commercials;
                }
                else
                {
                    var filtered = await _commercialService.SearchCommercialsAsync(searchText);
                    CommercialGrid.ItemsSource = filtered;
                }
                UpdateItemCount();
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to search commercials", ex);
            }
        }

        private string GetCurrentUsername()
        {
            return WindowsIdentity.GetCurrent()?.Name ?? "Unknown";
        }

        #region Event Handlers

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _ = SearchCommercialsAsync(SearchTextBox.Text);
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadCommercialsAsync();
            SearchTextBox.Clear();
        }

        private void CommercialGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = CommercialGrid.SelectedItem as Commercial;
            EditButton.IsEnabled = selected != null;
            DeleteButton.IsEnabled = selected != null;
            ScheduleButton.IsEnabled = selected != null;

            CommercialSelected?.Invoke(this, selected);
        }

        private void CommercialGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            EditButton_Click(sender, e);
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommercialDialog(_channel);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                await ProcessAddCommercial(dialog.Commercial, dialog.AudioFilePath);
            }
        }

        private async System.Threading.Tasks.Task ProcessAddCommercial(Commercial commercial, string audioFilePath)
        {
            try
            {
                IsEnabled = false;

                // Convert and replicate audio if provided
                if (!string.IsNullOrEmpty(audioFilePath))
                {
                    var spotName = Commercial.SanitizeForFilename(commercial.Spot);
                    var audioResult = await _audioService.ConvertAndReplicateAsync(audioFilePath, spotName, _channel);

                    if (!audioResult.Success)
                    {
                        MessageBox.Show($"Failed to process audio: {audioResult.ErrorMessage}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Update commercial with actual duration and filename
                    commercial.Duration = AudioService.FormatDuration(audioResult.Duration);
                    commercial.Filename = $"{spotName}.WAV";

                    // Show replication results
                    var failedReplications = audioResult.ReplicationResults.FindAll(r => !r.Success);
                    if (failedReplications.Count > 0)
                    {
                        var msg = $"Audio converted but failed to replicate to {failedReplications.Count} target(s):\n" +
                                  string.Join("\n", failedReplications.Select(r => $"- {r.TargetPath}: {r.ErrorMessage}"));
                        MessageBox.Show(msg, "Replication Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                // Save to database
                var result = await _commercialService.AddCommercialAsync(commercial, GetCurrentUsername());

                if (result.Success)
                {
                    await LoadCommercialsAsync();
                    MessageBox.Show($"Commercial '{commercial.Spot}' added successfully.",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to add commercial: {result.ErrorMessage}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = CommercialGrid.SelectedItem as Commercial;
            if (selected == null)
            {
                MessageBox.Show("Please select a commercial to edit.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new CommercialDialog(_channel, selected.Clone());
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                await ProcessUpdateCommercial(dialog.Commercial, selected.Spot, dialog.AudioFilePath);
            }
        }

        private async System.Threading.Tasks.Task ProcessUpdateCommercial(Commercial commercial, string originalSpot, string audioFilePath)
        {
            try
            {
                IsEnabled = false;

                // Convert and replicate new audio if provided
                if (!string.IsNullOrEmpty(audioFilePath))
                {
                    var spotName = Commercial.SanitizeForFilename(commercial.Spot);
                    var audioResult = await _audioService.ConvertAndReplicateAsync(audioFilePath, spotName, _channel);

                    if (!audioResult.Success)
                    {
                        MessageBox.Show($"Failed to process audio: {audioResult.ErrorMessage}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    commercial.Duration = AudioService.FormatDuration(audioResult.Duration);
                    commercial.Filename = $"{spotName}.WAV";

                    var failedReplications = audioResult.ReplicationResults.FindAll(r => !r.Success);
                    if (failedReplications.Count > 0)
                    {
                        var msg = $"Audio converted but failed to replicate to {failedReplications.Count} target(s).";
                        MessageBox.Show(msg, "Replication Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                // Update database
                var result = await _commercialService.UpdateCommercialAsync(commercial, originalSpot, GetCurrentUsername());

                if (result.Success)
                {
                    await LoadCommercialsAsync();
                    MessageBox.Show($"Commercial '{commercial.Spot}' updated successfully.",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to update commercial: {result.ErrorMessage}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = CommercialGrid.SelectedItem as Commercial;
            if (selected == null)
            {
                MessageBox.Show("Please select a commercial to delete.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Are you sure you want to delete commercial '{selected.Spot}'?\n\n" +
                "Note: Audio files will NOT be deleted from X targets.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                IsEnabled = false;
                var result = await _commercialService.DeleteCommercialAsync(selected.Spot);

                if (result.Success)
                {
                    await LoadCommercialsAsync();
                    MessageBox.Show($"Commercial '{selected.Spot}' deleted successfully.",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to delete commercial: {result.ErrorMessage}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private void ScheduleButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = CommercialGrid.SelectedItem as Commercial;
            if (selected != null)
            {
                ScheduleRequested?.Invoke(this, selected);
            }
        }

        #endregion

        #region Drag and Drop

        private void UserControl_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var hasAudio = files?.Any(f => AudioService.IsSupportedAudioFormat(f)) == true;

                if (hasAudio)
                {
                    e.Effects = DragDropEffects.Copy;
                    DropOverlay.Visibility = Visibility.Visible;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private async void UserControl_Drop(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var audioFiles = files?.Where(f => AudioService.IsSupportedAudioFormat(f)).ToList();

            if (audioFiles == null || audioFiles.Count == 0)
                return;

            // Process each dropped audio file
            foreach (var audioFile in audioFiles)
            {
                var spotName = System.IO.Path.GetFileNameWithoutExtension(audioFile);
                spotName = Commercial.SanitizeForFilename(spotName);

                // Show dialog to complete commercial details
                var commercial = new Commercial
                {
                    Spot = spotName,
                    Title = spotName,
                    Filename = $"{spotName}.WAV"
                };

                var dialog = new CommercialDialog(_channel, commercial, audioFile);
                dialog.Owner = Window.GetWindow(this);

                if (dialog.ShowDialog() == true)
                {
                    await ProcessAddCommercial(dialog.Commercial, audioFile);
                }
            }
        }

        #endregion
    }
}
