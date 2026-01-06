using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Controls
{
    /// <summary>
    /// Activity log viewer control
    /// </summary>
    public partial class ActivityLogControl : UserControl
    {
        private List<LogEntry> _allLogs = new List<LogEntry>();
        private readonly DispatcherTimer _refreshTimer;

        public ActivityLogControl()
        {
            InitializeComponent();

            // Setup refresh timer
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;

            // Subscribe to new log events
            LogService.LogAdded += OnLogAdded;

            Loaded += ActivityLogControl_Loaded;
            Unloaded += ActivityLogControl_Unloaded;
        }

        private void ActivityLogControl_Loaded(object sender, RoutedEventArgs e)
        {
            LoadLogs();
            _refreshTimer.Start();
        }

        private void ActivityLogControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _refreshTimer.Stop();
            LogService.LogAdded -= OnLogAdded;
        }

        private void LoadLogs()
        {
            _allLogs = LogService.GetRecentLogs(200);
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            // Guard against calls before control is fully loaded
            if (!IsLoaded || LogGrid == null || ShowInfoCheckBox == null ||
                ShowWarningsCheckBox == null || ShowErrorsCheckBox == null)
                return;

            var filtered = _allLogs.AsEnumerable();

            if (ShowInfoCheckBox.IsChecked != true)
            {
                filtered = filtered.Where(l => l.Level != "INF" && l.Level != "DBG");
            }

            if (ShowWarningsCheckBox.IsChecked != true)
            {
                filtered = filtered.Where(l => l.Level != "WRN");
            }

            if (ShowErrorsCheckBox.IsChecked != true)
            {
                filtered = filtered.Where(l => !l.IsError);
            }

            var filteredList = filtered.ToList();
            LogGrid.ItemsSource = filteredList;

            // Update counts
            if (LogCountLabel != null)
                LogCountLabel.Text = $"{filteredList.Count} entries";

            var errorCount = _allLogs.Count(l => l.IsError);
            if (ErrorCountLabel != null)
            {
                if (errorCount > 0)
                {
                    ErrorCountLabel.Text = $"{errorCount} error(s)";
                    ErrorCountLabel.Visibility = Visibility.Visible;
                }
                else
                {
                    ErrorCountLabel.Visibility = Visibility.Collapsed;
                }
            }

            // Auto-scroll to bottom
            if (AutoScrollCheckBox?.IsChecked == true && filteredList.Count > 0)
            {
                LogGrid.ScrollIntoView(filteredList[filteredList.Count - 1]);
            }
        }

        private void OnLogAdded(object sender, LogEntry entry)
        {
            Dispatcher.Invoke(() =>
            {
                _allLogs.Add(entry);

                // Limit list size
                while (_allLogs.Count > 500)
                {
                    _allLogs.RemoveAt(0);
                }

                ApplyFilter();
            });
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            // Just update the view in case logs were added from other threads
            ApplyFilter();
        }

        #region Event Handlers

        private void FilterCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadLogs();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Clear the in-memory log buffer?\n\n(Log files on disk will not be affected)",
                "Clear Logs",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                LogService.ClearRecentLogs();
                _allLogs.Clear();
                ApplyFilter();
                ExceptionPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void LogGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LogGrid.SelectedItem is LogEntry entry && !string.IsNullOrEmpty(entry.Exception))
            {
                ExceptionTextBox.Text = entry.Exception;
                ExceptionPanel.Visibility = Visibility.Visible;
            }
            else
            {
                ExceptionPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "AirStudio",
                "CommercialManager",
                "Logs");

            try
            {
                if (Directory.Exists(logDirectory))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = logDirectory,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show($"Log folder not found:\n{logDirectory}",
                        "Folder Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open log folder:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}
