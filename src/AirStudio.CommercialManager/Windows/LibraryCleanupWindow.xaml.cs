using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Library;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Windows
{
    /// <summary>
    /// Window for cleaning up old commercials from the library
    /// </summary>
    public partial class LibraryCleanupWindow : Window
    {
        private readonly Channel _channel;
        private readonly CommercialService _commercialService;
        private readonly TrashService _trashService;
        private List<CleanupCommercialItem> _allItems = new List<CleanupCommercialItem>();
        private int? _selectedYear;

        public LibraryCleanupWindow(Channel channel)
        {
            InitializeComponent();
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _commercialService = new CommercialService(channel);
            _trashService = new TrashService(channel);
            ChannelLabel.Text = $"Channel: {channel.Name}";
            Title = $"Library Cleanup - {channel.Name}";
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadYearsAsync();
            await LoadCommercialsAsync();
        }

        private async System.Threading.Tasks.Task LoadYearsAsync()
        {
            try
            {
                var years = await _commercialService.GetAvailableYearsAsync();

                YearComboBox.Items.Clear();
                YearComboBox.Items.Add(new ComboBoxItem { Content = "All Years", Tag = null });

                foreach (var year in years)
                {
                    YearComboBox.Items.Add(new ComboBoxItem { Content = year.ToString(), Tag = year });
                }

                YearComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load years", ex);
            }
        }

        private async System.Threading.Tasks.Task LoadCommercialsAsync()
        {
            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;

                var commercials = await _commercialService.GetCommercialsByYearAsync(_selectedYear);

                _allItems = commercials.Select(c => new CleanupCommercialItem(c, _channel)).ToList();

                ApplyStatusFilter();
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load commercials for cleanup", ex);
                MessageBox.Show($"Failed to load commercials: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyStatusFilter()
        {
            var statusFilter = (StatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

            IEnumerable<CleanupCommercialItem> filtered = _allItems;

            if (statusFilter == "Active Only")
            {
                filtered = _allItems.Where(i => i.IsActive);
            }

            var list = filtered.ToList();
            CommercialsDataGrid.ItemsSource = list;
            CountLabel.Text = $"({list.Count} items)";
            UpdateSelectionSummary();
        }

        private void YearComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard against calls during InitializeComponent
            if (!IsLoaded) return;

            if (YearComboBox.SelectedItem is ComboBoxItem item)
            {
                _selectedYear = item.Tag as int?;
                _ = LoadCommercialsAsync();
            }
        }

        private void StatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard against calls during InitializeComponent
            if (!IsLoaded) return;
            ApplyStatusFilter();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadCommercialsAsync();
        }

        private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var isChecked = SelectAllCheckBox.IsChecked == true;
            var visibleItems = CommercialsDataGrid.ItemsSource as List<CleanupCommercialItem>;

            if (visibleItems != null)
            {
                foreach (var item in visibleItems)
                {
                    item.IsSelected = isChecked;
                }
                CommercialsDataGrid.Items.Refresh();
                UpdateSelectionSummary();
            }
        }

        private void ItemCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateSelectionSummary();
        }

        private void UpdateSelectionSummary()
        {
            var selected = _allItems.Where(i => i.IsSelected).ToList();
            var count = selected.Count;
            var totalSize = selected.Sum(i => i.FileSizeBytes);

            SelectionSummaryLabel.Text = $"Selected: {count} commercial{(count != 1 ? "s" : "")}";
            SizeSummaryLabel.Text = count > 0 ? $"(Total: {FormatSize(totalSize)})" : "";
            MoveToTrashButton.IsEnabled = count > 0;
        }

        private string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        }

        private async void MoveToTrashButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = _allItems.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Please select at least one commercial to move to trash.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Build confirmation message
            var message = $"You are about to move {selected.Count} commercial{(selected.Count != 1 ? "s" : "")} to trash:\n\n";

            if (selected.Count <= 5)
            {
                foreach (var item in selected)
                {
                    message += $"  - {item.Spot}\n";
                }
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    message += $"  - {selected[i].Spot}\n";
                }
                message += $"  ... and {selected.Count - 3} more\n";
            }

            message += "\nFiles will be moved to trash in all X target locations.\n";
            message += "Items can be restored within 30 days.";

            var result = MessageBox.Show(message, "Confirm Move to Trash",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;

                var commercials = selected.Select(i => i.Commercial).ToList();
                var trashResult = await _trashService.MoveToTrashAsync(commercials);

                if (trashResult.Success)
                {
                    var msg = $"Successfully moved {trashResult.SuccessCount} commercial{(trashResult.SuccessCount != 1 ? "s" : "")} to trash.";

                    if (trashResult.Errors.Count > 0)
                    {
                        msg += $"\n\n{trashResult.Errors.Count} error(s) occurred:\n{string.Join("\n", trashResult.Errors.Take(3))}";
                    }

                    MessageBox.Show(msg, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadCommercialsAsync();
                }
                else
                {
                    MessageBox.Show($"Failed to move items to trash:\n\n{trashResult.ErrorSummary}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to move items to trash", ex);
                MessageBox.Show($"Error moving items to trash: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void ViewTrashButton_Click(object sender, RoutedEventArgs e)
        {
            var trashWindow = new TrashViewerWindow(_channel);
            trashWindow.Owner = this;
            trashWindow.ShowDialog();

            // Refresh after trash window closes in case items were restored
            await LoadCommercialsAsync();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>
    /// Wrapper for Commercial with selection and file size info for cleanup grid
    /// </summary>
    public class CleanupCommercialItem
    {
        public Commercial Commercial { get; }

        public int Id => Commercial.Id;
        public string Spot => Commercial.Spot;
        public string Agency => Commercial.Agency;
        public string Duration => Commercial.Duration;
        public DateTime DateIn => Commercial.DateIn;
        public string Status => Commercial.Status;
        public bool IsActive => Commercial.IsActive;

        public bool IsSelected { get; set; }
        public long FileSizeBytes { get; }

        public CleanupCommercialItem(Commercial commercial, Channel channel)
        {
            Commercial = commercial;

            // Try to get file size from primary path
            if (!string.IsNullOrEmpty(channel.PrimaryCommercialsPath) && !string.IsNullOrEmpty(commercial.Filename))
            {
                var filePath = Path.Combine(channel.PrimaryCommercialsPath, commercial.Filename);
                if (File.Exists(filePath))
                {
                    var info = new FileInfo(filePath);
                    FileSizeBytes = info.Length;
                }
            }
        }
    }
}
