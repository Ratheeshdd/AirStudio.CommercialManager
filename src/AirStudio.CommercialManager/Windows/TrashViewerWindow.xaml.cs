using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Library;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Windows
{
    /// <summary>
    /// Window for viewing and managing trash items
    /// </summary>
    public partial class TrashViewerWindow : Window
    {
        private readonly Channel _channel;
        private readonly TrashService _trashService;
        private List<TrashItem> _trashItems = new List<TrashItem>();

        public TrashViewerWindow(Channel channel)
        {
            InitializeComponent();
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _trashService = new TrashService(channel);
            Title = $"Trash - {channel.Name}";
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadTrashItemsAsync();
        }

        private async System.Threading.Tasks.Task LoadTrashItemsAsync()
        {
            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;

                // Auto-purge expired items first
                await _trashService.AutoPurgeExpiredAsync();

                // Load trash items
                _trashItems = await _trashService.GetTrashItemsAsync();

                // Load statistics
                var stats = await _trashService.GetStatisticsAsync();

                TrashSummaryLabel.Text = $"{_trashItems.Count} item{(_trashItems.Count != 1 ? "s" : "")}";
                TotalSizeLabel.Text = stats.TotalSizeDisplay;
                ExpiringTodayLabel.Text = stats.ExpiringToday.ToString();
                ExpiringSoonLabel.Text = stats.ExpiringSoon.ToString();

                TrashDataGrid.ItemsSource = _trashItems;
                EmptyState.Visibility = _trashItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                EmptyTrashButton.IsEnabled = _trashItems.Count > 0;

                UpdateSelectionUI();
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load trash items", ex);
                MessageBox.Show($"Failed to load trash: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ItemCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateSelectionUI();
        }

        private void UpdateSelectionUI()
        {
            var selectedCount = _trashItems.Count(i => i.IsSelected);
            SelectionLabel.Text = $"Selected: {selectedCount} item{(selectedCount != 1 ? "s" : "")}";
            RestoreButton.IsEnabled = selectedCount > 0;
            DeleteButton.IsEnabled = selectedCount > 0;
        }

        private async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = _trashItems.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0) return;

            var result = MessageBox.Show(
                $"Are you sure you want to restore {selected.Count} item{(selected.Count != 1 ? "s" : "")} from trash?\n\n" +
                "Files will be moved back to the commercials folder.",
                "Confirm Restore",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;

                var restoreResult = await _trashService.RestoreFromTrashAsync(selected);

                if (restoreResult.Success)
                {
                    var msg = $"Successfully restored {restoreResult.SuccessCount} item{(restoreResult.SuccessCount != 1 ? "s" : "")}.";

                    if (restoreResult.Errors.Count > 0)
                    {
                        msg += $"\n\n{restoreResult.Errors.Count} error(s):\n{string.Join("\n", restoreResult.Errors.Take(3))}";
                    }

                    MessageBox.Show(msg, "Restore Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadTrashItemsAsync();
                }
                else
                {
                    MessageBox.Show($"Failed to restore items:\n\n{restoreResult.ErrorSummary}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to restore items from trash", ex);
                MessageBox.Show($"Error restoring items: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = _trashItems.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0) return;

            var result = MessageBox.Show(
                $"Are you sure you want to PERMANENTLY DELETE {selected.Count} item{(selected.Count != 1 ? "s" : "")}?\n\n" +
                "This action CANNOT be undone!",
                "Confirm Permanent Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;

                var deleteResult = await _trashService.PermanentDeleteAsync(selected);

                if (deleteResult.Success)
                {
                    var msg = $"Permanently deleted {deleteResult.SuccessCount} item{(deleteResult.SuccessCount != 1 ? "s" : "")}.";

                    if (deleteResult.Errors.Count > 0)
                    {
                        msg += $"\n\n{deleteResult.Errors.Count} error(s):\n{string.Join("\n", deleteResult.Errors.Take(3))}";
                    }

                    MessageBox.Show(msg, "Delete Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadTrashItemsAsync();
                }
                else
                {
                    MessageBox.Show($"Failed to delete items:\n\n{deleteResult.ErrorSummary}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to permanently delete items", ex);
                MessageBox.Show($"Error deleting items: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void EmptyTrashButton_Click(object sender, RoutedEventArgs e)
        {
            if (_trashItems.Count == 0)
            {
                MessageBox.Show("Trash is already empty.", "Empty Trash",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to PERMANENTLY DELETE ALL {_trashItems.Count} item{(_trashItems.Count != 1 ? "s" : "")} in trash?\n\n" +
                "This action CANNOT be undone!",
                "Empty Trash",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;

                var deleteResult = await _trashService.PermanentDeleteAsync(_trashItems);

                if (deleteResult.Success)
                {
                    MessageBox.Show($"Trash emptied. Deleted {deleteResult.SuccessCount} item{(deleteResult.SuccessCount != 1 ? "s" : "")}.",
                        "Trash Emptied", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadTrashItemsAsync();
                }
                else
                {
                    MessageBox.Show($"Failed to empty trash:\n\n{deleteResult.ErrorSummary}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to empty trash", ex);
                MessageBox.Show($"Error emptying trash: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
