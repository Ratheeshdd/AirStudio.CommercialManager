using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Logging;
using AirStudio.CommercialManager.Core.Services.Tags;
using AirStudio.CommercialManager.Windows;

namespace AirStudio.CommercialManager.Controls
{
    /// <summary>
    /// Control for scheduling a capsule
    /// </summary>
    public partial class SchedulingControl : UserControl
    {
        private Channel _channel;
        private Capsule _capsule;
        private TagService _tagService;

        /// <summary>
        /// Event raised when user wants to edit the capsule
        /// </summary>
        public event EventHandler<Capsule> EditCapsuleRequested;

        /// <summary>
        /// Event raised when scheduling is completed
        /// </summary>
        public event EventHandler<Schedule> ScheduleCompleted;

        public SchedulingControl()
        {
            InitializeComponent();

            // Set default dates
            FromDatePicker.SelectedDate = DateTime.Today;
            ToDatePicker.SelectedDate = DateTime.Today;
        }

        /// <summary>
        /// Initialize with a channel and capsule
        /// </summary>
        public void Initialize(Channel channel, Capsule capsule)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _capsule = capsule ?? throw new ArgumentNullException(nameof(capsule));
            _tagService = new TagService(channel);

            // Update UI
            UpdateCapsuleDisplay();
            UpdateRepeatInfo();
            ValidateSchedule();

            // Load waveform preview
            if (_capsule.HasSegments)
            {
                var segments = _capsule.GetSegmentAudioPaths();
                if (segments.Count > 0)
                {
                    PreviewWaveform.LoadCapsule(_capsule.Name ?? "Capsule", segments);
                }
            }
        }

        private void UpdateCapsuleDisplay()
        {
            if (_capsule == null)
            {
                CapsuleNameLabel.Text = "--";
                CapsuleInfoLabel.Text = "";
                SegmentsGrid.ItemsSource = null;
                return;
            }

            CapsuleNameLabel.Text = _capsule.Name ?? "(unnamed)";
            CapsuleInfoLabel.Text = $"{_capsule.SegmentCount} segment(s), {_capsule.TotalDurationFormatted} total";
            SegmentsGrid.ItemsSource = _capsule.Segments;
        }

        private void UpdateRepeatInfo()
        {
            var fromDate = FromDatePicker.SelectedDate ?? DateTime.Today;
            var toDate = ToDatePicker.SelectedDate ?? DateTime.Today;

            var repeatDays = Math.Max(0, (int)(toDate - fromDate).TotalDays);
            RepeatDaysLabel.Text = repeatDays.ToString();

            if (repeatDays == 0)
            {
                RepeatInfoLabel.Text = "(single day)";
            }
            else
            {
                RepeatInfoLabel.Text = $"(plays for {repeatDays + 1} days)";
            }
        }

        private bool ValidateSchedule()
        {
            var schedule = BuildSchedule();
            var error = schedule.GetValidationError();

            if (string.IsNullOrEmpty(error))
            {
                ValidationLabel.Text = "";
                ScheduleButton.IsEnabled = true;
                return true;
            }
            else
            {
                ValidationLabel.Text = error;
                ScheduleButton.IsEnabled = false;
                return false;
            }
        }

        private Schedule BuildSchedule()
        {
            int.TryParse(HourTextBox.Text, out var hours);
            int.TryParse(MinuteTextBox.Text, out var minutes);
            int.TryParse(SecondTextBox.Text, out var seconds);

            // Clamp values
            hours = Math.Max(0, Math.Min(23, hours));
            minutes = Math.Max(0, Math.Min(59, minutes));
            seconds = Math.Max(0, Math.Min(59, seconds));

            return new Schedule
            {
                Capsule = _capsule,
                TxTime = new TimeSpan(hours, minutes, seconds),
                FromDate = FromDatePicker.SelectedDate ?? DateTime.Today,
                ToDate = ToDatePicker.SelectedDate ?? DateTime.Today,
                UserName = UserNameTextBox.Text?.Trim() ?? "",
                MobileNo = MobileNoTextBox.Text?.Trim() ?? ""
            };
        }

        #region Event Handlers

        private void TimeTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Only allow digits
            e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
        }

        private void TimeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateSchedule();
        }

        private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            // Ensure ToDate is not before FromDate
            if (FromDatePicker.SelectedDate.HasValue && ToDatePicker.SelectedDate.HasValue)
            {
                if (ToDatePicker.SelectedDate < FromDatePicker.SelectedDate)
                {
                    ToDatePicker.SelectedDate = FromDatePicker.SelectedDate;
                }
            }

            UpdateRepeatInfo();
            ValidateSchedule();
        }

        private void EditCapsuleButton_Click(object sender, RoutedEventArgs e)
        {
            EditCapsuleRequested?.Invoke(this, _capsule);
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateSchedule())
                return;

            var schedule = BuildSchedule();
            var tagFile = schedule.CreateTagFile(_channel.PrimaryCommercialsPath ?? "");

            // Show preview in a dialog
            var previewWindow = new TagPreviewWindow(tagFile);
            previewWindow.Owner = Window.GetWindow(this);
            previewWindow.ShowDialog();
        }

        private async void ScheduleButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateSchedule())
                return;

            var schedule = BuildSchedule();

            // Check for existing schedule at same time
            var exists = await _tagService.PlaylistRowExistsAsync(schedule.FromDate, schedule.TxTime);
            if (exists)
            {
                var result = MessageBox.Show(
                    $"A schedule already exists at {schedule.TxTime:hh\\:mm\\:ss} on {schedule.FromDate:dd/MM/yyyy}.\n\n" +
                    "Do you want to replace it?",
                    "Schedule Exists",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                ScheduleButton.IsEnabled = false;
                ScheduleButton.Content = "Scheduling...";

                // Create TAG file
                var tagFile = schedule.CreateTagFile(_channel.PrimaryCommercialsPath ?? "");

                // Save TAG to all X targets
                var saveResult = await _tagService.SaveTagFileAsync(tagFile);

                if (!saveResult.AnySucceeded)
                {
                    MessageBox.Show(
                        $"Failed to save TAG file:\n\n{saveResult.GetSummary()}",
                        "Save Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // Save playlist row to database
                var dbResult = await _tagService.SavePlaylistRowAsync(
                    tagFile,
                    schedule.UserName,
                    schedule.MobileNo);

                if (!dbResult.Success)
                {
                    MessageBox.Show(
                        $"TAG file saved but database update failed:\n\n{dbResult.ErrorMessage}\n\n" +
                        "The TAG file has been created, but the schedule may not appear in the playlist.",
                        "Database Warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    var msg = $"Successfully scheduled:\n\n" +
                              $"Capsule: {schedule.Capsule.Name}\n" +
                              $"Time: {schedule.TxTime:hh\\:mm\\:ss}\n" +
                              $"Date: {schedule.FromDate:dd/MM/yyyy}";

                    if (schedule.IsRepeating)
                    {
                        msg += $" to {schedule.ToDate:dd/MM/yyyy}";
                    }

                    msg += $"\n\nTAG: {tagFile.FileName}";

                    if (!saveResult.AllSucceeded)
                    {
                        msg += $"\n\nWarning: Saved to {saveResult.SuccessCount} of {saveResult.SuccessCount + saveResult.FailCount} targets.";
                    }

                    MessageBox.Show(msg, "Schedule Created", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                LogService.Info($"Scheduled: {schedule}");

                // Raise completion event
                ScheduleCompleted?.Invoke(this, schedule);
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to create schedule", ex);
                MessageBox.Show(
                    $"Failed to create schedule:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ScheduleButton.IsEnabled = true;
                ScheduleButton.Content = "Schedule";
            }
        }

        #endregion
    }
}
