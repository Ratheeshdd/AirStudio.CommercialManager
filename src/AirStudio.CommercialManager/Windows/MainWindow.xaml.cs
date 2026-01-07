using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Audio;
using AirStudio.CommercialManager.Core.Services.Channels;
using AirStudio.CommercialManager.Core.Services.Configuration;
using AirStudio.CommercialManager.Core.Services.Library;
using AirStudio.CommercialManager.Core.Services.Logging;
using AirStudio.CommercialManager.Core.Services.Tags;

namespace AirStudio.CommercialManager.Windows
{
    public partial class MainWindow : Window
    {
        private AppConfiguration _config;
        private List<Channel> _channels = new List<Channel>();
        private Channel _selectedChannel;
        private object _previousChannelSelection;

        // Services
        private CommercialService _commercialService;
        private TagService _tagService;

        // Data collections
        private List<ScheduledCommercial> _allScheduledCommercials = new List<ScheduledCommercial>();
        private List<Commercial> _allCommercials = new List<Commercial>();
        private ObservableCollection<PlaylistItem> _playlistItems = new ObservableCollection<PlaylistItem>();

        // Drag-drop support
        private Point _dragStartPoint;
        private bool _isDragging;

        // Waveform context
        private enum WaveformContext { None, LibraryItem, Schedule, Playlist }
        private WaveformContext _currentWaveformContext = WaveformContext.None;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            ChannelComboBox.SelectionChanged += ChannelComboBox_SelectionChanged;
            KeyDown += MainWindow_KeyDown;

            // Initialize playlist grid
            PlaylistGrid.ItemsSource = _playlistItems;
            _playlistItems.CollectionChanged += PlaylistItems_CollectionChanged;

            // Set default dates
            TxDatePicker.SelectedDate = DateTime.Today;
            ToDatePicker.SelectedDate = DateTime.Today;

            // Subscribe to waveform viewer events
            WaveformViewer.StateChanged += WaveformViewer_StateChanged;
            WaveformViewer.PositionChanged += WaveformViewer_PositionChanged;
            WaveformViewer.PlaybackCompleted += WaveformViewer_PlaybackCompleted;

            // Setup toolbar button popup animations
            SetupToolbarPopups();
        }

        private void SetupToolbarPopups()
        {
            // Broadcast Sheet button popup
            BroadcastSheetButton.MouseEnter += (s, e) => BroadcastSheetPopup.IsOpen = true;
            BroadcastSheetButton.MouseLeave += (s, e) => BroadcastSheetPopup.IsOpen = false;

            // Agencies button popup
            AgenciesButton.MouseEnter += (s, e) => AgenciesPopup.IsOpen = true;
            AgenciesButton.MouseLeave += (s, e) => AgenciesPopup.IsOpen = false;

            // Library Cleanup button popup
            LibraryCleanupButton.MouseEnter += (s, e) => LibraryCleanupPopup.IsOpen = true;
            LibraryCleanupButton.MouseLeave += (s, e) => LibraryCleanupPopup.IsOpen = false;

            // Activity Log button popup
            ActivityLogButton.MouseEnter += (s, e) => ActivityLogPopup.IsOpen = true;
            ActivityLogButton.MouseLeave += (s, e) => ActivityLogPopup.IsOpen = false;

            // Refresh All button popup
            RefreshAllButton.MouseEnter += (s, e) => RefreshAllPopup.IsOpen = true;
            RefreshAllButton.MouseLeave += (s, e) => RefreshAllPopup.IsOpen = false;
        }

        #region Window Events

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var currentUser = WindowsIdentity.GetCurrent();
            UserLabel.Text = $"User: {currentUser.Name}";

            var principal = new WindowsPrincipal(currentUser);
            bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            ConfigButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

            LogService.Info($"MainWindow loaded. User: {currentUser.Name}, IsAdmin: {isAdmin}");

            try
            {
                _config = await ConfigurationService.Instance.LoadAsync();
                UpdateDbStatus(_config.IsValid);
                await LoadChannelsAsync();
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load configuration", ex);
                UpdateDbStatus(false);
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (HasUnsavedChanges())
            {
                var result = MessageBox.Show(
                    "You have unsaved changes in the Playlist Creator.\n\nDo you want to save before closing?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                switch (result)
                {
                    case MessageBoxResult.Yes:
                        if (!TrySaveSchedule())
                        {
                            e.Cancel = true;
                        }
                        break;
                    case MessageBoxResult.Cancel:
                        e.Cancel = true;
                        break;
                }
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                RefreshAllButton_Click(sender, e);
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (SaveScheduleButton.IsEnabled)
                    SaveScheduleButton_Click(sender, e);
            }
        }

        #endregion

        #region Channel Management

        private async Task LoadChannelsAsync()
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
                }
                else
                {
                    StatusLabel.Text = "No channels found. Check database connection.";
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

            if (_previousChannelSelection != null && _previousChannelSelection != selectedItem)
            {
                if (HasUnsavedChanges())
                {
                    var result = MessageBox.Show(
                        "You have unsaved changes. Do you want to discard them?",
                        "Unsaved Changes",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No)
                    {
                        ChannelComboBox.SelectionChanged -= ChannelComboBox_SelectionChanged;
                        ChannelComboBox.SelectedItem = _previousChannelSelection;
                        ChannelComboBox.SelectionChanged += ChannelComboBox_SelectionChanged;
                        return;
                    }
                }
            }

            _previousChannelSelection = selectedItem;

            if (selectedItem?.Tag is Channel channel)
            {
                _selectedChannel = channel;
                await OnChannelSelectedAsync(channel);
            }
            else
            {
                _selectedChannel = null;
                DisableAllPanels();
            }
        }

        private async Task OnChannelSelectedAsync(Channel channel)
        {
            LogService.Info($"Channel selected: {channel.Name}");

            // Show the selected channel indicator
            SelectedGlow.Visibility = Visibility.Visible;

            // Initialize services
            _commercialService = new CommercialService(channel);
            _tagService = new TagService(channel);

            // Update UI
            StatusLabel.Text = $"Loading data for {channel.Name}...";
            var accessibleTargets = channel.GetAccessibleTargets();
            XTargetLabel.Text = $"X Targets: {accessibleTargets.Count}/{channel.XRootTargets.Count}";

            // Enable toolbar buttons
            EnableToolbarButtons(true);

            // Clear playlist
            ClearPlaylist();

            // Load data for all panels
            await RefreshAllPanelsAsync();

            StatusLabel.Text = $"Channel: {channel.Name}";
        }

        private void EnableToolbarButtons(bool enabled)
        {
            BroadcastSheetButton.IsEnabled = enabled;
            AgenciesButton.IsEnabled = enabled;
            LibraryCleanupButton.IsEnabled = enabled;
            RefreshAllButton.IsEnabled = enabled;
            AddCommercialButton.IsEnabled = enabled;
        }

        private void DisableAllPanels()
        {
            // Hide the selected channel indicator
            SelectedGlow.Visibility = Visibility.Collapsed;

            EnableToolbarButtons(false);
            XTargetLabel.Text = "X Targets: --";

            _allScheduledCommercials.Clear();
            _allCommercials.Clear();
            ClearPlaylist();

            ScheduledGrid.ItemsSource = null;
            LibraryGrid.ItemsSource = null;

            ScheduledCountLabel.Text = " (0 items)";
            LibraryCountLabel.Text = " (0 items)";
        }

        #endregion

        #region Data Loading

        private async Task RefreshAllPanelsAsync()
        {
            await Task.WhenAll(
                RefreshScheduledCommercialsAsync(),
                RefreshLibraryAsync()
            );
        }

        private async Task RefreshScheduledCommercialsAsync()
        {
            if (_commercialService == null) return;

            try
            {
                _allScheduledCommercials = await _commercialService.LoadScheduledCommercialsAsync();
                ApplyScheduleFilters();
                ScheduledCountLabel.Text = $" ({_allScheduledCommercials.Count} items)";
                ScheduledEmptyState.Visibility = _allScheduledCommercials.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load scheduled commercials", ex);
            }
        }

        private async Task RefreshLibraryAsync()
        {
            if (_commercialService == null) return;

            try
            {
                _allCommercials = await _commercialService.LoadCommercialsAsync();
                ApplyLibraryFilter();
                LibraryCountLabel.Text = $" ({_allCommercials.Count} items)";
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load commercials", ex);
            }
        }

        private void ApplyScheduleFilters()
        {
            var filtered = _allScheduledCommercials.AsEnumerable();

            // Search filter
            var searchText = ScheduleSearchTextBox.Text?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(searchText))
            {
                filtered = filtered.Where(s =>
                    (s.CapsuleName?.ToLowerInvariant().Contains(searchText) == true) ||
                    (s.UserName?.ToLowerInvariant().Contains(searchText) == true));
            }

            // Date filters
            if (ScheduleFromDatePicker.SelectedDate.HasValue)
            {
                filtered = filtered.Where(s => s.TxDate >= ScheduleFromDatePicker.SelectedDate.Value);
            }
            if (ScheduleToDatePicker.SelectedDate.HasValue)
            {
                filtered = filtered.Where(s => s.TxDate <= ScheduleToDatePicker.SelectedDate.Value);
            }

            ScheduledGrid.ItemsSource = filtered.ToList();
        }

        private void ApplyLibraryFilter()
        {
            var searchText = LibrarySearchTextBox.Text?.Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(searchText))
            {
                LibraryGrid.ItemsSource = _allCommercials;
            }
            else
            {
                var filtered = _allCommercials.Where(c =>
                    (c.Spot?.ToLowerInvariant().Contains(searchText) == true) ||
                    (c.Title?.ToLowerInvariant().Contains(searchText) == true) ||
                    (c.Agency?.ToLowerInvariant().Contains(searchText) == true))
                    .ToList();
                LibraryGrid.ItemsSource = filtered;
            }
        }

        #endregion

        #region Scheduled Commercials Panel

        private void ScheduleSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyScheduleFilters();
        }

        private void ScheduleDateFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            ApplyScheduleFilters();
        }

        private void RefreshSchedulesButton_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshScheduledCommercialsAsync();
        }

        private void ScheduledGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScheduledGrid.SelectedItem is ScheduledCommercial schedule)
            {
                // Stop current playback and reset cursor
                WaveformViewer.Stop();

                // Load capsule waveform for the schedule
                LoadWaveformForSchedule(schedule);

                // Enable preview button if audio was loaded
                PreviewButton.IsEnabled = WaveformViewer.HasAudio;
                StopButton.IsEnabled = false;
            }
        }

        private void ScheduledGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            EditSchedule_Click(sender, e);
        }

        private void EditSchedule_Click(object sender, RoutedEventArgs e)
        {
            if (ScheduledGrid.SelectedItem is ScheduledCommercial schedule)
            {
                LoadScheduleForEditing(schedule);
            }
        }

        private async void DeleteSchedule_Click(object sender, RoutedEventArgs e)
        {
            if (ScheduledGrid.SelectedItem is ScheduledCommercial schedule)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete the schedule:\n\n{schedule.CapsuleName}\n{schedule.TxDate:dd-MMM-yyyy} at {schedule.TxTime}",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    var deleteResult = await _commercialService.DeleteScheduledCommercialAsync(schedule.Id);
                    if (deleteResult.Success)
                    {
                        StatusLabel.Text = "Schedule deleted successfully";
                        await RefreshScheduledCommercialsAsync();
                    }
                    else
                    {
                        MessageBox.Show($"Failed to delete schedule: {deleteResult.ErrorMessage}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void ViewTagFile_Click(object sender, RoutedEventArgs e)
        {
            if (ScheduledGrid.SelectedItem is ScheduledCommercial schedule)
            {
                if (!string.IsNullOrEmpty(schedule.TagFilePath) && File.Exists(schedule.TagFilePath))
                {
                    System.Diagnostics.Process.Start("notepad.exe", schedule.TagFilePath);
                }
                else
                {
                    MessageBox.Show($"TAG file not found.\nPath: {schedule.TagFilePath}",
                        "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void LoadScheduleForEditing(ScheduledCommercial schedule)
        {
            // Parse the TAG file to get the cuts
            if (!string.IsNullOrEmpty(schedule.TagFilePath) && File.Exists(schedule.TagFilePath))
            {
                try
                {
                    var tagFile = _tagService.LoadTagFile(schedule.TagFilePath);
                    if (tagFile == null)
                    {
                        MessageBox.Show("Failed to parse TAG file.",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Clear and populate playlist
                    ClearPlaylist();
                    CapsuleNameTextBox.Text = schedule.CapsuleName;
                    TxDatePicker.SelectedDate = schedule.TxDate;
                    ToDatePicker.SelectedDate = schedule.ToDate;
                    UserNameTextBox.Text = schedule.UserName;
                    MobileNoTextBox.Text = schedule.MobileNo;

                    // Parse time
                    var timeParts = schedule.TxTime?.Split(':') ?? new[] { "00", "00", "00" };
                    TxHourTextBox.Text = timeParts.Length > 0 ? timeParts[0] : "00";
                    TxMinuteTextBox.Text = timeParts.Length > 1 ? timeParts[1] : "00";
                    TxSecondTextBox.Text = timeParts.Length > 2 ? timeParts[2] : "00";

                    // Add items from TAG file using CommercialEntries (handles single-spot TAG files correctly)
                    int order = 1;
                    foreach (var entry in tagFile.CommercialEntries)
                    {
                        var commercial = _allCommercials.FirstOrDefault(c =>
                            string.Equals(c.Spot, entry.SpotName, StringComparison.OrdinalIgnoreCase));

                        if (commercial != null)
                        {
                            _playlistItems.Add(new PlaylistItem(commercial, order++));
                        }
                        else
                        {
                            LogService.Warning($"Commercial not found in library for spot: '{entry.SpotName}'");
                        }
                    }

                    UpdatePlaylistUI();
                    StatusLabel.Text = $"Loaded schedule for editing: {schedule.CapsuleName}";
                }
                catch (Exception ex)
                {
                    LogService.Error("Failed to load schedule for editing", ex);
                    MessageBox.Show($"Failed to load TAG file: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Library Panel

        private void LibrarySearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyLibraryFilter();
        }

        private void RefreshLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshLibraryAsync();
        }

        private void AddCommercialButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedChannel == null) return;

            var dialog = new CommercialDialog(_selectedChannel);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                _ = RefreshLibraryAsync();
                StatusLabel.Text = "Commercial added successfully";
            }
        }

        private void LibraryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LibraryGrid.SelectedItem is Commercial commercial)
            {
                // Stop current playback and reset cursor
                WaveformViewer.Stop();

                // Load waveform for selected commercial
                LoadWaveformForCommercial(commercial);

                // Enable preview button if audio was loaded
                PreviewButton.IsEnabled = WaveformViewer.HasAudio;
                StopButton.IsEnabled = false;
            }
        }

        private void LibraryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            AddSelectedLibraryItemToPlaylist();
        }

        private void AddSelectedLibraryItemToPlaylist()
        {
            var selectedItems = LibraryGrid.SelectedItems.Cast<Commercial>().ToList();
            foreach (var commercial in selectedItems)
            {
                AddCommercialToPlaylist(commercial);
            }
        }

        #endregion

        #region Library Drag-Drop

        private void LibraryGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void LibraryGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                var currentPos = e.GetPosition(null);
                var diff = _dragStartPoint - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var selectedItems = LibraryGrid.SelectedItems.Cast<Commercial>().ToList();
                    if (selectedItems.Any())
                    {
                        _isDragging = true;
                        var data = new DataObject("Commercials", selectedItems);
                        DragDrop.DoDragDrop(LibraryGrid, data, DragDropEffects.Copy);
                        _isDragging = false;
                    }
                }
            }
        }

        private void LibraryPanel_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var audioExtensions = new[] { ".wav", ".mp3", ".aiff", ".m4a", ".wma", ".ogg", ".flac" };
                if (files.Any(f => audioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
                {
                    e.Effects = DragDropEffects.Copy;
                    LibraryDropOverlay.Visibility = Visibility.Visible;
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

        private void LibraryPanel_Drop(object sender, DragEventArgs e)
        {
            LibraryDropOverlay.Visibility = Visibility.Collapsed;

            if (e.Data.GetDataPresent(DataFormats.FileDrop) && _selectedChannel != null)
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var audioExtensions = new[] { ".wav", ".mp3", ".aiff", ".m4a", ".wma", ".ogg", ".flac" };
                var audioFiles = files.Where(f => audioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();

                foreach (var file in audioFiles)
                {
                    var newCommercial = new Commercial();
                    var dialog = new CommercialDialog(_selectedChannel, newCommercial, file);
                    dialog.Owner = this;
                    if (dialog.ShowDialog() == true)
                    {
                        _ = RefreshLibraryAsync();
                    }
                }
            }
        }

        #endregion

        #region Playlist Panel

        private void AddCommercialToPlaylist(Commercial commercial)
        {
            if (commercial == null) return;

            var order = _playlistItems.Count + 1;
            _playlistItems.Add(new PlaylistItem(commercial, order));
            UpdatePlaylistUI();

            // Auto-set capsule name if first item and name is empty
            if (_playlistItems.Count == 1 && string.IsNullOrWhiteSpace(CapsuleNameTextBox.Text))
            {
                CapsuleNameTextBox.Text = commercial.Spot;
            }
        }

        private void UpdatePlaylistUI()
        {
            // Renumber items
            for (int i = 0; i < _playlistItems.Count; i++)
            {
                _playlistItems[i].Order = i + 1;
            }

            // Update total duration
            var totalDuration = TimeSpan.Zero;
            foreach (var item in _playlistItems)
            {
                totalDuration += item.Duration;
            }
            TotalDurationLabel.Text = totalDuration.ToString(@"hh\:mm\:ss");

            // Update UI state
            var hasItems = _playlistItems.Count > 0;
            PlaylistEmptyHint.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
            ClearPlaylistButton.IsEnabled = hasItems;
            PreviewButton.IsEnabled = hasItems;
            SaveScheduleButton.IsEnabled = hasItems;

            // Update integrated waveform time display
            WaveformTotalTime.Text = totalDuration.ToString(@"hh\:mm\:ss");
            WaveformCurrentTime.Text = "00:00:00";

            // Refresh grid
            PlaylistGrid.Items.Refresh();

            // Update waveform
            if (hasItems)
            {
                UpdatePlaylistWaveform();
            }
        }

        private void ClearPlaylist()
        {
            _playlistItems.Clear();
            CapsuleNameTextBox.Text = "";
            TxHourTextBox.Text = "00";
            TxMinuteTextBox.Text = "00";
            TxSecondTextBox.Text = "00";
            TxDatePicker.SelectedDate = DateTime.Today;
            ToDatePicker.SelectedDate = DateTime.Today;
            UserNameTextBox.Text = "";
            MobileNoTextBox.Text = "";
            TotalDurationLabel.Text = "00:00:00";
            ValidationLabel.Text = "";
            UpdatePlaylistUI();
            WaveformViewer.Clear();
        }

        private void PlaylistItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdatePlaylistUI();
        }

        private void CapsuleNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidatePlaylist();
        }

        private void PlaylistGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var hasSelection = PlaylistGrid.SelectedItem != null;
            var selectedIndex = PlaylistGrid.SelectedIndex;

            MoveUpButton.IsEnabled = hasSelection && selectedIndex > 0;
            MoveDownButton.IsEnabled = hasSelection && selectedIndex < _playlistItems.Count - 1;
            RemoveItemButton.IsEnabled = hasSelection;

            // Update waveform when playlist item is selected
            if (hasSelection && PlaylistGrid.SelectedItem is PlaylistItem item && item.Commercial != null)
            {
                // Stop current playback and reset cursor
                WaveformViewer.Stop();

                // Load waveform for selected item
                LoadWaveformForCommercial(item.Commercial);

                // Enable preview button if audio was loaded
                PreviewButton.IsEnabled = WaveformViewer.HasAudio;
                StopButton.IsEnabled = false;
            }
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            var index = PlaylistGrid.SelectedIndex;
            if (index > 0)
            {
                var item = _playlistItems[index];
                _playlistItems.RemoveAt(index);
                _playlistItems.Insert(index - 1, item);
                PlaylistGrid.SelectedIndex = index - 1;
                UpdatePlaylistUI();
            }
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            var index = PlaylistGrid.SelectedIndex;
            if (index < _playlistItems.Count - 1)
            {
                var item = _playlistItems[index];
                _playlistItems.RemoveAt(index);
                _playlistItems.Insert(index + 1, item);
                PlaylistGrid.SelectedIndex = index + 1;
                UpdatePlaylistUI();
            }
        }

        private void RemoveItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlaylistGrid.SelectedItem is PlaylistItem item)
            {
                _playlistItems.Remove(item);
                UpdatePlaylistUI();
            }
        }

        private void ClearPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            if (_playlistItems.Count > 0)
            {
                var result = MessageBox.Show(
                    "Are you sure you want to clear the playlist?",
                    "Clear Playlist",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ClearPlaylist();
                }
            }
        }

        #endregion

        #region Playlist Drag-Drop

        private void PlaylistPanel_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("Commercials"))
            {
                e.Effects = DragDropEffects.Copy;
                PlaylistDropOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void PlaylistPanel_Drop(object sender, DragEventArgs e)
        {
            PlaylistDropOverlay.Visibility = Visibility.Collapsed;

            if (e.Data.GetDataPresent("Commercials"))
            {
                var commercials = e.Data.GetData("Commercials") as List<Commercial>;
                if (commercials != null)
                {
                    foreach (var commercial in commercials)
                    {
                        AddCommercialToPlaylist(commercial);
                    }
                }
            }
        }

        private void PlaylistGrid_DragOver(object sender, DragEventArgs e)
        {
            PlaylistPanel_DragOver(sender, e);
        }

        private void PlaylistGrid_Drop(object sender, DragEventArgs e)
        {
            PlaylistPanel_Drop(sender, e);
        }

        #endregion

        #region Waveform Management

        private void LoadWaveformForCommercial(Commercial commercial)
        {
            if (_selectedChannel == null || commercial == null) return;

            _currentWaveformContext = WaveformContext.LibraryItem;
            var audioPath = Path.Combine(_selectedChannel.PrimaryCommercialsPath ?? "", commercial.Filename ?? "");

            if (File.Exists(audioPath))
            {
                WaveformViewer.LoadAudio(audioPath);
            }
            else
            {
                WaveformViewer.Clear();
            }
        }

        private void LoadWaveformForSchedule(ScheduledCommercial schedule)
        {
            if (_selectedChannel == null || schedule == null) return;

            _currentWaveformContext = WaveformContext.Schedule;

            // Try to load from TAG file and show multi-segment waveform
            if (!string.IsNullOrEmpty(schedule.TagFilePath) && File.Exists(schedule.TagFilePath))
            {
                try
                {
                    var tagFile = _tagService.LoadTagFile(schedule.TagFilePath);
                    if (tagFile == null) return;
                    var audioPaths = new List<string>();

                    foreach (var entry in tagFile.CommercialEntries)
                    {
                        if (!string.IsNullOrEmpty(entry.AudioPath) && File.Exists(entry.AudioPath))
                        {
                            audioPaths.Add(entry.AudioPath);
                        }
                    }

                    if (audioPaths.Count > 0)
                    {
                        WaveformViewer.LoadMultipleAudio(audioPaths);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    LogService.Warning($"Failed to parse TAG file for waveform: {ex.Message}");
                }
            }

            WaveformViewer.Clear();
        }

        private void UpdatePlaylistWaveform()
        {
            if (_selectedChannel == null || _playlistItems.Count == 0)
            {
                WaveformViewer.Clear();
                return;
            }

            _currentWaveformContext = WaveformContext.Playlist;

            var audioPaths = new List<string>();
            foreach (var item in _playlistItems)
            {
                var path = Path.Combine(_selectedChannel.PrimaryCommercialsPath ?? "", item.Commercial?.Filename ?? "");
                if (File.Exists(path))
                {
                    audioPaths.Add(path);
                }
            }

            if (audioPaths.Count > 0)
            {
                WaveformViewer.LoadMultipleAudio(audioPaths);
            }
            else
            {
                WaveformViewer.Clear();
            }
        }

        #endregion

        #region Save & Schedule

        private bool HasUnsavedChanges()
        {
            return _playlistItems.Count > 0 ||
                   !string.IsNullOrWhiteSpace(CapsuleNameTextBox.Text) ||
                   !string.IsNullOrWhiteSpace(UserNameTextBox.Text);
        }

        private bool ValidatePlaylist()
        {
            ValidationLabel.Text = "";

            if (_playlistItems.Count == 0)
            {
                ValidationLabel.Text = "Add at least one commercial to the playlist";
                return false;
            }

            if (string.IsNullOrWhiteSpace(CapsuleNameTextBox.Text))
            {
                ValidationLabel.Text = "Capsule name is required";
                return false;
            }

            if (!TxDatePicker.SelectedDate.HasValue)
            {
                ValidationLabel.Text = "Transmission date is required";
                return false;
            }

            if (!ToDatePicker.SelectedDate.HasValue)
            {
                ValidationLabel.Text = "To date is required";
                return false;
            }

            if (ToDatePicker.SelectedDate < TxDatePicker.SelectedDate)
            {
                ValidationLabel.Text = "To date must be on or after transmission date";
                return false;
            }

            if (string.IsNullOrWhiteSpace(UserNameTextBox.Text))
            {
                ValidationLabel.Text = "User name is required";
                return false;
            }

            if (string.IsNullOrWhiteSpace(MobileNoTextBox.Text))
            {
                ValidationLabel.Text = "Mobile number is required";
                return false;
            }

            return true;
        }

        private async void SaveScheduleButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidatePlaylist()) return;

            await SaveScheduleAsync();
        }

        private bool TrySaveSchedule()
        {
            if (!ValidatePlaylist()) return false;

            var task = SaveScheduleAsync();
            task.Wait();
            return true;
        }

        private async Task SaveScheduleAsync()
        {
            if (_selectedChannel == null || _tagService == null) return;

            try
            {
                StatusLabel.Text = "Saving schedule...";
                SaveScheduleButton.IsEnabled = false;

                // Build capsule
                var capsule = new Capsule { Name = CapsuleNameTextBox.Text.Trim() };
                foreach (var item in _playlistItems)
                {
                    var audioPath = Path.Combine(_selectedChannel.PrimaryCommercialsPath ?? "", item.Commercial?.Filename ?? "");
                    capsule.AddCommercial(item.Commercial, audioPath);
                }

                // Parse time components
                int.TryParse(TxHourTextBox.Text, out int hours);
                int.TryParse(TxMinuteTextBox.Text, out int minutes);
                int.TryParse(TxSecondTextBox.Text, out int seconds);
                var txTimeSpan = new TimeSpan(hours, minutes, seconds);
                var txTimeStr = txTimeSpan.ToString(@"hh\:mm\:ss");

                // Build schedule
                var schedule = new Schedule
                {
                    Capsule = capsule,
                    TxTime = txTimeSpan,
                    FromDate = TxDatePicker.SelectedDate ?? DateTime.Today,
                    ToDate = ToDatePicker.SelectedDate ?? DateTime.Today,
                    UserName = UserNameTextBox.Text.Trim(),
                    MobileNo = MobileNoTextBox.Text.Trim()
                };

                // Create TAG file from schedule
                var tagFile = schedule.CreateTagFile(_selectedChannel.PrimaryCommercialsPath);

                // Save TAG file to all X targets
                var tagResult = await _tagService.SaveTagFileAsync(tagFile);

                if (!tagResult.AnySucceeded)
                {
                    StatusLabel.Text = "Failed to save TAG file";
                    MessageBox.Show($"Failed to save TAG file:\n\n{tagResult.GetSummary()}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Save to database
                var dbResult = await _tagService.SavePlaylistRowAsync(tagFile, schedule.UserName, schedule.MobileNo);

                if (dbResult.Success || tagResult.AnySucceeded)
                {
                    StatusLabel.Text = $"Schedule saved: {capsule.Name}";
                    LogService.Info($"Schedule saved: {capsule.Name} at {txTimeStr}");

                    // Refresh scheduled commercials grid
                    await RefreshScheduledCommercialsAsync();

                    // Clear playlist after successful save
                    ClearPlaylist();

                    var summary = $"Schedule saved successfully!\n\nCapsule: {capsule.Name}\nTime: {txTimeStr}\nFrom: {schedule.FromDate:dd-MMM-yyyy}\nTo: {schedule.ToDate:dd-MMM-yyyy}";
                    if (!dbResult.Success)
                    {
                        summary += $"\n\nWarning: Database save had issues: {dbResult.ErrorMessage}";
                    }

                    MessageBox.Show(summary, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusLabel.Text = "Failed to save schedule";
                    MessageBox.Show($"Failed to save schedule:\n\n{dbResult.ErrorMessage}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to save schedule", ex);
                StatusLabel.Text = "Error saving schedule";
                MessageBox.Show($"Error saving schedule:\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SaveScheduleButton.IsEnabled = _playlistItems.Count > 0;
            }
        }

        #endregion

        #region Preview Playback

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (WaveformViewer.HasAudio)
            {
                WaveformViewer.Play();
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            WaveformViewer.Stop();
        }

        private void WaveformViewer_StateChanged(object sender, PlaybackState state)
        {
            Dispatcher.Invoke(() =>
            {
                switch (state)
                {
                    case PlaybackState.Playing:
                        PreviewButton.Content = "\u23F8"; // Pause symbol
                        StopButton.IsEnabled = true;
                        break;
                    case PlaybackState.Paused:
                        PreviewButton.Content = "\u25B6"; // Play symbol
                        break;
                    case PlaybackState.Stopped:
                        PreviewButton.Content = "\u25B6"; // Play symbol
                        StopButton.IsEnabled = false;
                        WaveformCurrentTime.Text = "00:00:00";
                        break;
                }
            });
        }

        private void WaveformViewer_PositionChanged(object sender, TimeSpan position)
        {
            Dispatcher.Invoke(() =>
            {
                WaveformCurrentTime.Text = position.ToString(@"hh\:mm\:ss");
                WaveformTotalTime.Text = WaveformViewer.Duration.ToString(@"hh\:mm\:ss");
            });
        }

        private void WaveformViewer_PlaybackCompleted(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                PreviewButton.Content = "\u25B6"; // Play symbol
                StopButton.IsEnabled = false;
                WaveformCurrentTime.Text = "00:00:00";
            });
        }

        #endregion

        #region Time Input Validation

        private void TimeTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !char.IsDigit(e.Text, 0);
        }

        #endregion

        #region Toolbar Actions

        private void RefreshAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedChannel != null)
            {
                _ = RefreshAllPanelsAsync();
                StatusLabel.Text = "Refreshing all panels...";
            }
        }

        private void BroadcastSheetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedChannel == null) return;

            var window = new BroadcastSheetWindow(_selectedChannel);
            window.Owner = this;
            window.ShowDialog();
        }

        private void AgenciesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedChannel == null) return;

            var window = new AgencyManagementWindow(_selectedChannel);
            window.Owner = this;
            window.ShowDialog();

            // Refresh library in case agencies were modified
            _ = RefreshLibraryAsync();
        }

        private void LibraryCleanupButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedChannel == null) return;

            var window = new LibraryCleanupWindow(_selectedChannel);
            window.Owner = this;
            window.ShowDialog();

            // Refresh library in case items were deleted
            _ = RefreshLibraryAsync();
        }

        private void ActivityLogButton_Click(object sender, RoutedEventArgs e)
        {
            var logWindow = new ActivityLogWindow();
            logWindow.Owner = this;
            logWindow.Show();
        }

        private void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            var configWindow = new ConfigurationWindow();
            configWindow.Owner = this;
            configWindow.ShowDialog();
            MainWindow_Loaded(sender, e);
        }

        #endregion

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
            Close();
        }

        #endregion

        #region Helper Methods

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

        #endregion
    }
}
