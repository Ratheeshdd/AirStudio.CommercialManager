using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Channels;
using AirStudio.CommercialManager.Core.Services.Configuration;
using AirStudio.CommercialManager.Core.Services.Logging;
using AirStudio.CommercialManager.Interfaces;

namespace AirStudio.CommercialManager.Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private AppConfiguration _config;
        private List<Channel> _channels = new List<Channel>();
        private Channel _selectedChannel;
        private object _previousChannelSelection;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            ChannelComboBox.SelectionChanged += ChannelComboBox_SelectionChanged;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Display current user
            var currentUser = WindowsIdentity.GetCurrent();
            UserLabel.Text = $"User: {currentUser.Name}";

            // Check if user is a local administrator
            var principal = new WindowsPrincipal(currentUser);
            bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);

            // Show config button only for admins
            ConfigButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

            LogService.Info($"MainWindow loaded. User: {currentUser.Name}, IsAdmin: {isAdmin}");

            // Load configuration
            try
            {
                _config = await ConfigurationService.Instance.LoadAsync();
                UpdateDbStatus(_config.IsValid);

                // Load channels from database
                await LoadChannelsAsync();
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load configuration", ex);
                UpdateDbStatus(false);
            }
        }

        private async System.Threading.Tasks.Task LoadChannelsAsync()
        {
            try
            {
                StatusLabel.Text = "Loading channels...";

                _channels = await ChannelService.Instance.GetChannelsAsync(forceRefresh: true);

                ChannelComboBox.Items.Clear();
                foreach (var channel in _channels)
                {
                    ChannelComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = channel.DisplayName,
                        Tag = channel,
                        IsEnabled = channel.IsUsable
                    });
                }

                if (_channels.Count > 0)
                {
                    var usableCount = _channels.FindAll(c => c.IsUsable).Count;
                    StatusLabel.Text = $"Loaded {_channels.Count} channels ({usableCount} usable)";
                    LogService.Info($"Loaded {_channels.Count} channels, {usableCount} usable");
                }
                else
                {
                    StatusLabel.Text = "No channels found. Check database connection.";
                    LogService.Warning("No channels loaded from database");
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load channels", ex);
                StatusLabel.Text = "Failed to load channels";
            }
        }

        private async void ChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = ChannelComboBox.SelectedItem as ComboBoxItem;

            // Check for unsaved changes before switching channels
            if (_previousChannelSelection != null && _previousChannelSelection != selectedItem)
            {
                var canProceed = await CheckAndHandleUnsavedChangesAsync("switch channels");
                if (!canProceed)
                {
                    // Revert to previous selection
                    ChannelComboBox.SelectionChanged -= ChannelComboBox_SelectionChanged;
                    ChannelComboBox.SelectedItem = _previousChannelSelection;
                    ChannelComboBox.SelectionChanged += ChannelComboBox_SelectionChanged;
                    return;
                }
            }

            _previousChannelSelection = selectedItem;

            if (selectedItem?.Tag is Channel channel)
            {
                _selectedChannel = channel;
                OnChannelSelected(channel);
            }
            else
            {
                _selectedChannel = null;
                DisableAllActions();
            }
        }

        #region Unsaved Changes Protection

        /// <summary>
        /// Gets the current active control that tracks unsaved changes, if any
        /// </summary>
        private IUnsavedChangesTracker GetCurrentUnsavedChangesTracker()
        {
            return WorkAreaContent.Child as IUnsavedChangesTracker;
        }

        /// <summary>
        /// Checks if there are unsaved changes and prompts user to Save/Discard/Cancel
        /// </summary>
        /// <param name="actionDescription">Description of what the user is trying to do (e.g., "switch channels", "close the application")</param>
        /// <returns>True if the action can proceed, false if cancelled</returns>
        private async Task<bool> CheckAndHandleUnsavedChangesAsync(string actionDescription)
        {
            var tracker = GetCurrentUnsavedChangesTracker();
            if (tracker == null || !tracker.HasUnsavedChanges)
            {
                return true; // No unsaved changes, proceed
            }

            var result = MessageBox.Show(
                $"You have unsaved changes in {tracker.UnsavedChangesDescription}.\n\n" +
                $"Do you want to save before you {actionDescription}?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    // Try to save
                    var saved = await tracker.SaveChangesAsync();
                    if (!saved)
                    {
                        // Save failed or was cancelled, don't proceed
                        MessageBox.Show(
                            "Changes could not be saved. The operation has been cancelled.",
                            "Save Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return false;
                    }
                    return true;

                case MessageBoxResult.No:
                    // Discard changes and proceed
                    tracker.DiscardChanges();
                    return true;

                case MessageBoxResult.Cancel:
                default:
                    // Don't proceed
                    return false;
            }
        }

        /// <summary>
        /// Synchronous version for use in Closing event
        /// </summary>
        private bool CheckAndHandleUnsavedChanges(string actionDescription)
        {
            var tracker = GetCurrentUnsavedChangesTracker();
            if (tracker == null || !tracker.HasUnsavedChanges)
            {
                return true;
            }

            var result = MessageBox.Show(
                $"You have unsaved changes in {tracker.UnsavedChangesDescription}.\n\n" +
                $"Do you want to save before you {actionDescription}?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    // Try to save synchronously
                    var saveTask = tracker.SaveChangesAsync();
                    saveTask.Wait();
                    if (!saveTask.Result)
                    {
                        MessageBox.Show(
                            "Changes could not be saved. The operation has been cancelled.",
                            "Save Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return false;
                    }
                    return true;

                case MessageBoxResult.No:
                    tracker.DiscardChanges();
                    return true;

                case MessageBoxResult.Cancel:
                default:
                    return false;
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            var canClose = CheckAndHandleUnsavedChanges("close the application");
            if (!canClose)
            {
                e.Cancel = true;
                LogService.Info("Window close cancelled due to unsaved changes");
            }
        }

        #endregion

        private void OnChannelSelected(Channel channel)
        {
            LogService.Info($"Channel selected: {channel.Name}");

            // Update UI
            WorkAreaTitle.Text = $"CHANNEL: {channel.Name.ToUpperInvariant()}";
            StatusLabel.Text = $"Channel: {channel.Name}";

            // Update X target status
            var accessibleTargets = channel.GetAccessibleTargets();
            XTargetLabel.Text = $"X Targets: {accessibleTargets.Count}/{channel.XRootTargets.Count}";

            // Enable action buttons
            EnableActionsForChannel(channel);
        }

        private void EnableActionsForChannel(Channel channel)
        {
            LibraryButton.IsEnabled = true;
            AgencyButton.IsEnabled = true;
            CapsuleButton.IsEnabled = true;
            ScheduleButton.IsEnabled = true;
            ImportTagButton.IsEnabled = true;
            ViewScheduleButton.IsEnabled = true;
        }

        private void DisableAllActions()
        {
            WorkAreaTitle.Text = "SELECT A CHANNEL TO BEGIN";
            XTargetLabel.Text = "X Targets: --";

            LibraryButton.IsEnabled = false;
            AgencyButton.IsEnabled = false;
            CapsuleButton.IsEnabled = false;
            ScheduleButton.IsEnabled = false;
            ImportTagButton.IsEnabled = false;
            ViewScheduleButton.IsEnabled = false;
        }

        private void UpdateDbStatus(bool connected)
        {
            if (connected)
            {
                DbStatusLabel.Text = "DB Connected";
                DbStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            }
            else
            {
                DbStatusLabel.Text = "DB Disconnected";
                DbStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            }
        }

        #region Title Bar Handlers

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaximizeButton_Click(sender, e);
            }
            else
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "☐";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // The Closing event handler will check for unsaved changes
            Close();
        }

        #endregion

        #region Navigation Handlers

        private void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            LogService.Info("Configuration button clicked");

            var configWindow = new ConfigurationWindow();
            configWindow.Owner = this;
            configWindow.ShowDialog();

            // Reload configuration after closing
            MainWindow_Loaded(sender, e);
        }

        #endregion

        #region Action Button Handlers

        private async void AgencyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedChannel == null)
            {
                MessageBox.Show("Please select a channel first.", "No Channel Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!await CheckAndHandleUnsavedChangesAsync("navigate to Agency Management"))
                return;

            ShowAgencyManagement();
        }

        private void ShowAgencyManagement()
        {
            WorkAreaTitle.Text = $"AGENCY MANAGEMENT - {_selectedChannel.Name.ToUpperInvariant()}";

            var agencyControl = new Controls.AgencyManagementControl();
            agencyControl.Initialize(_selectedChannel);

            WorkAreaContent.Child = agencyControl;
            StatusLabel.Text = $"Managing agencies for {_selectedChannel.Name}";
        }

        private async void LibraryButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedChannel == null) return;

            if (!await CheckAndHandleUnsavedChangesAsync("navigate to Commercial Library"))
                return;

            WorkAreaTitle.Text = $"COMMERCIAL LIBRARY - {_selectedChannel.Name.ToUpperInvariant()}";

            var libraryControl = new Controls.LibraryControl();
            libraryControl.CommercialSelected += (s, commercial) =>
            {
                UpdatePreviewPanel(commercial);
            };
            libraryControl.ScheduleRequested += (s, commercial) =>
            {
                // TODO: Open scheduling dialog for this commercial
                MessageBox.Show($"Schedule '{commercial?.Spot}' - Coming soon", "Schedule", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            libraryControl.Initialize(_selectedChannel);

            WorkAreaContent.Child = libraryControl;
            StatusLabel.Text = $"Library: {_selectedChannel.Name}";
        }

        private void UpdatePreviewPanel(Core.Models.Commercial commercial)
        {
            if (commercial == null)
            {
                ClearPreviewPanel();
                return;
            }

            // Update properties
            PropertySpot.Text = $"Spot: {commercial.Spot}";
            PropertyTitle.Text = $"Title: {commercial.Title ?? "--"}";
            PropertyAgency.Text = $"Agency: {commercial.Agency ?? "--"}";
            PropertyDuration.Text = $"Duration: {commercial.Duration ?? "--"}";
            PropertyFilename.Text = $"File: {commercial.Filename ?? "--"}";

            // Load waveform if we have a file and channel
            if (_selectedChannel != null && !string.IsNullOrEmpty(commercial.Filename))
            {
                var audioPath = System.IO.Path.Combine(
                    _selectedChannel.PrimaryCommercialsPath ?? "",
                    commercial.Filename);

                if (System.IO.File.Exists(audioPath))
                {
                    WaveformViewer.LoadAudio(audioPath);
                }
                else
                {
                    WaveformViewer.Clear();
                }
            }
        }

        private void ClearPreviewPanel()
        {
            PropertySpot.Text = "Spot: --";
            PropertyTitle.Text = "Title: --";
            PropertyAgency.Text = "Agency: --";
            PropertyDuration.Text = "Duration: --";
            PropertyFilename.Text = "File: --";
            WaveformViewer.Clear();
        }

        private async void CapsuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedChannel == null) return;

            if (!await CheckAndHandleUnsavedChangesAsync("navigate to Build Capsule"))
                return;

            WorkAreaTitle.Text = $"BUILD CAPSULE - {_selectedChannel.Name.ToUpperInvariant()}";

            var capsuleBuilder = new Controls.CapsuleBuilderControl();
            capsuleBuilder.CapsuleReady += (s, capsule) =>
            {
                // Navigate to scheduling with this capsule
                ShowSchedulingForCapsule(capsule);
            };
            capsuleBuilder.Initialize(_selectedChannel);

            WorkAreaContent.Child = capsuleBuilder;
            StatusLabel.Text = $"Building capsule for {_selectedChannel.Name}";
        }

        private void ShowSchedulingForCapsule(Core.Models.Capsule capsule)
        {
            if (capsule == null || _selectedChannel == null) return;

            // Mark the current capsule builder as clean since we're proceeding with the capsule
            if (WorkAreaContent.Child is Controls.CapsuleBuilderControl capsuleBuilder)
            {
                capsuleBuilder.MarkClean();
            }

            WorkAreaTitle.Text = $"SCHEDULE: {capsule.Name.ToUpperInvariant()}";

            var schedulingControl = new Controls.SchedulingControl();
            schedulingControl.EditCapsuleRequested += (s, c) =>
            {
                // Go back to capsule builder (no unsaved changes check since scheduling hasn't saved yet)
                NavigateToCapsuleBuilderForEdit(c);
            };
            schedulingControl.ScheduleCompleted += (s, schedule) =>
            {
                StatusLabel.Text = $"Scheduled: {schedule.ToDisplayString()}";
                // Could navigate to schedule view here
            };
            schedulingControl.Initialize(_selectedChannel, capsule);

            WorkAreaContent.Child = schedulingControl;
            StatusLabel.Text = $"Scheduling capsule '{capsule.Name}' ({capsule.SegmentCount} segments)";
        }

        private void NavigateToCapsuleBuilderForEdit(Core.Models.Capsule capsule)
        {
            // When editing capsule from scheduling, we don't need unsaved changes check
            // because the scheduling hasn't been completed yet
            WorkAreaTitle.Text = $"BUILD CAPSULE - {_selectedChannel.Name.ToUpperInvariant()}";

            var capsuleBuilder = new Controls.CapsuleBuilderControl();
            capsuleBuilder.CapsuleReady += (s, c) =>
            {
                ShowSchedulingForCapsule(c);
            };
            capsuleBuilder.Initialize(_selectedChannel, capsule);

            WorkAreaContent.Child = capsuleBuilder;
            StatusLabel.Text = $"Editing capsule for {_selectedChannel.Name}";
        }

        private async void ScheduleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedChannel == null) return;

            if (!await CheckAndHandleUnsavedChangesAsync("navigate to Schedule Commercial"))
                return;

            // Open capsule builder - user needs to build or select a capsule first
            WorkAreaTitle.Text = $"SCHEDULE COMMERCIAL - {_selectedChannel.Name.ToUpperInvariant()}";

            var capsuleBuilder = new Controls.CapsuleBuilderControl();
            capsuleBuilder.CapsuleReady += (s, capsule) =>
            {
                ShowSchedulingForCapsule(capsule);
            };
            capsuleBuilder.Initialize(_selectedChannel);

            WorkAreaContent.Child = capsuleBuilder;
            StatusLabel.Text = $"Build or select capsule to schedule for {_selectedChannel.Name}";
        }

        private async void ImportTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedChannel == null) return;

            if (!await CheckAndHandleUnsavedChangesAsync("navigate to Import/Edit TAG"))
                return;

            WorkAreaTitle.Text = $"IMPORT/EDIT TAG - {_selectedChannel.Name.ToUpperInvariant()}";

            var tagEditor = new Controls.TagEditorControl();
            tagEditor.Initialize(_selectedChannel);

            WorkAreaContent.Child = tagEditor;
            StatusLabel.Text = $"Editing TAGs for {_selectedChannel.Name}";
        }

        private async void ViewScheduleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedChannel == null) return;

            if (!await CheckAndHandleUnsavedChangesAsync("navigate to View Schedule"))
                return;

            // TODO: Implement schedule view
            WorkAreaTitle.Text = $"VIEW SCHEDULE - {_selectedChannel.Name.ToUpperInvariant()}";
            StatusLabel.Text = $"Schedule view for {_selectedChannel.Name} (coming soon)";
        }

        private async void ActivityLogButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await CheckAndHandleUnsavedChangesAsync("navigate to Activity Log"))
                return;

            WorkAreaTitle.Text = "ACTIVITY LOG";

            var activityLog = new Controls.ActivityLogControl();
            WorkAreaContent.Child = activityLog;

            StatusLabel.Text = "Viewing activity log";
            LogService.Info("Activity log panel opened");
        }

        #endregion
    }
}