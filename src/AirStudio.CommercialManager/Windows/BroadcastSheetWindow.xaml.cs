using System;
using System.Globalization;
using System.Windows;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Logging;
using AirStudio.CommercialManager.Core.Services.Reports;
using Microsoft.Win32;

namespace AirStudio.CommercialManager.Windows
{
    /// <summary>
    /// Window for generating broadcast sheet PDF exports
    /// </summary>
    public partial class BroadcastSheetWindow : Window
    {
        private readonly Channel _channel;
        private readonly BroadcastSheetService _broadcastSheetService;

        public BroadcastSheetWindow(Channel channel)
        {
            InitializeComponent();
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _broadcastSheetService = new BroadcastSheetService(channel);

            ChannelLabel.Text = $"Channel: {channel.Name}";
            Title = $"Broadcast Sheet - {channel.Name}";

            // Set default dates
            FromDatePicker.SelectedDate = DateTime.Today;
            ToDatePicker.SelectedDate = DateTime.Today;
        }

        private void TodayButton_Click(object sender, RoutedEventArgs e)
        {
            ExportForDateRange(DateTime.Today, DateTime.Today, "Today");
        }

        private void ThisWeekButton_Click(object sender, RoutedEventArgs e)
        {
            // Get Monday of current week
            var today = DateTime.Today;
            var diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            var monday = today.AddDays(-diff);
            var sunday = monday.AddDays(6);

            ExportForDateRange(monday, sunday, "This_Week");
        }

        private void ThisMonthButton_Click(object sender, RoutedEventArgs e)
        {
            var today = DateTime.Today;
            var firstDay = new DateTime(today.Year, today.Month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);

            ExportForDateRange(firstDay, lastDay, $"{today:MMMM_yyyy}");
        }

        private void ThisYearButton_Click(object sender, RoutedEventArgs e)
        {
            var today = DateTime.Today;
            var firstDay = new DateTime(today.Year, 1, 1);
            var lastDay = new DateTime(today.Year, 12, 31);

            ExportForDateRange(firstDay, lastDay, $"Year_{today.Year}");
        }

        private void CustomExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (!FromDatePicker.SelectedDate.HasValue || !ToDatePicker.SelectedDate.HasValue)
            {
                MessageBox.Show("Please select both From and To dates.", "Invalid Selection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var fromDate = FromDatePicker.SelectedDate.Value;
            var toDate = ToDatePicker.SelectedDate.Value;

            if (toDate < fromDate)
            {
                MessageBox.Show("To date must be on or after From date.", "Invalid Selection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var suffix = fromDate.Date == toDate.Date
                ? $"{fromDate:yyyy-MM-dd}"
                : $"{fromDate:yyyy-MM-dd}_to_{toDate:yyyy-MM-dd}";

            ExportForDateRange(fromDate, toDate, suffix);
        }

        private async void ExportForDateRange(DateTime fromDate, DateTime toDate, string fileNameSuffix)
        {
            // Get output path from user
            var saveDialog = new SaveFileDialog
            {
                Title = "Save Broadcast Sheet",
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"BroadcastSheet_{_channel.Name}_{fileNameSuffix}.pdf",
                DefaultExt = "pdf"
            };

            if (saveDialog.ShowDialog() != true)
                return;

            try
            {
                ShowProgress("Generating broadcast sheet...");

                var options = new BroadcastSheetOptions
                {
                    IncludeSpotDetails = IncludeSpotDetailsCheckBox.IsChecked == true,
                    IncludeAgencyInfo = IncludeAgencyInfoCheckBox.IsChecked == true,
                    IncludeUserInfo = IncludeUserInfoCheckBox.IsChecked == true
                };

                // Get data
                var data = await _broadcastSheetService.GetBroadcastSheetDataAsync(fromDate, toDate, options);

                // Update preview (labels are now separate TextBlocks in XAML)
                PreviewDateRange.Text = data.PeriodDisplay;
                PreviewScheduleCount.Text = data.Summary.TotalSchedules.ToString();

                if (data.Summary.TotalSchedules == 0)
                {
                    HideProgress();
                    var result = MessageBox.Show(
                        "No schedules found for the selected date range.\n\nDo you still want to generate the PDF?",
                        "No Data",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                        return;

                    ShowProgress("Generating PDF...");
                }

                // Generate PDF
                _broadcastSheetService.GeneratePdf(data, saveDialog.FileName);

                HideProgress();

                var openResult = MessageBox.Show(
                    $"Broadcast sheet saved successfully!\n\n{saveDialog.FileName}\n\nDo you want to open it now?",
                    "Export Complete",
                    MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (openResult == MessageBoxResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = saveDialog.FileName,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        LogService.Warning($"Failed to open PDF: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                HideProgress();
                LogService.Error("Failed to generate broadcast sheet", ex);
                MessageBox.Show($"Failed to generate broadcast sheet:\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowProgress(string message)
        {
            StatusText.Text = message;
            StatusBorder.Visibility = Visibility.Visible;
            ProgressBar.IsIndeterminate = true;
        }

        private void HideProgress()
        {
            StatusBorder.Visibility = Visibility.Collapsed;
            ProgressBar.IsIndeterminate = false;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
