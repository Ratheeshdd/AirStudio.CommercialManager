using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Library;
using AirStudio.CommercialManager.Core.Services.Logging;
using AirStudio.CommercialManager.Interfaces;

namespace AirStudio.CommercialManager.Controls
{
    /// <summary>
    /// Capsule builder control for assembling commercials into capsules
    /// </summary>
    public partial class CapsuleBuilderControl : UserControl, IUnsavedChangesTracker
    {
        private Channel _channel;
        private CommercialService _commercialService;
        private Capsule _capsule = new Capsule();
        private List<Commercial> _libraryCommercials = new List<Commercial>();
        private Point _dragStartPoint;
        private bool _isDragging;
        private bool _isDirty;
        private readonly DispatcherTimer _searchDebounce;

        /// <summary>
        /// Event raised when capsule is ready to schedule
        /// </summary>
        public event EventHandler<Capsule> CapsuleReady;

        /// <summary>
        /// Event raised when capsule changes
        /// </summary>
        public event EventHandler<Capsule> CapsuleChanged;

        #region IUnsavedChangesTracker Implementation

        /// <summary>
        /// Gets whether the capsule has unsaved changes (segments added but not scheduled)
        /// </summary>
        public bool HasUnsavedChanges => _isDirty && _capsule.HasSegments;

        /// <summary>
        /// Gets a description of the unsaved changes
        /// </summary>
        public string UnsavedChangesDescription =>
            _capsule.HasSegments
                ? $"Capsule '{_capsule.Name ?? "Unnamed"}' with {_capsule.SegmentCount} segment(s)"
                : "Capsule builder";

        /// <summary>
        /// Save changes - for capsule builder, this means schedule the capsule
        /// </summary>
        public Task<bool> SaveChangesAsync()
        {
            // Capsule builder doesn't save directly; it schedules
            // This will trigger the CapsuleReady event if valid
            if (TryGetCapsule(out var capsule))
            {
                CapsuleReady?.Invoke(this, capsule);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        /// <summary>
        /// Discard unsaved changes
        /// </summary>
        public void DiscardChanges()
        {
            _capsule.Clear();
            CapsuleNameTextBox.Text = "";
            _isDirty = false;
            UpdateUI();
            CapsuleWaveformViewer.Clear();
            LogService.Info("Discarded capsule changes");
        }

        /// <summary>
        /// Mark the capsule as clean (no unsaved changes)
        /// </summary>
        public void MarkClean()
        {
            _isDirty = false;
        }

        #endregion

        public CapsuleBuilderControl()
        {
            InitializeComponent();

            // Setup search debounce
            _searchDebounce = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _searchDebounce.Tick += SearchDebounce_Tick;
        }

        /// <summary>
        /// Get the current capsule
        /// </summary>
        public Capsule Capsule => _capsule;

        /// <summary>
        /// Get the current channel
        /// </summary>
        public Channel Channel => _channel;

        /// <summary>
        /// Initialize the control with a channel
        /// </summary>
        public async void Initialize(Channel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _commercialService = new CommercialService(channel);
            _capsule = new Capsule();
            _isDirty = false;

            await LoadLibraryAsync();
            UpdateUI();
        }

        /// <summary>
        /// Initialize with existing capsule for editing
        /// </summary>
        public async void Initialize(Channel channel, Capsule existingCapsule)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _commercialService = new CommercialService(channel);
            _capsule = existingCapsule ?? new Capsule();
            _isDirty = false;

            CapsuleNameTextBox.Text = _capsule.Name ?? "";

            await LoadLibraryAsync();
            UpdateUI();
            UpdateWaveformPreview();
        }

        private async System.Threading.Tasks.Task LoadLibraryAsync()
        {
            try
            {
                _libraryCommercials = await _commercialService.GetCommercialsAsync(forceRefresh: true);
                LibraryListBox.ItemsSource = _libraryCommercials;
                LogService.Info($"Loaded {_libraryCommercials.Count} commercials for capsule builder");
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load library for capsule builder", ex);
                MessageBox.Show($"Failed to load library: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateUI()
        {
            // Update segment list
            SegmentsListBox.ItemsSource = null;
            SegmentsListBox.ItemsSource = _capsule.Segments;

            // Update totals
            TotalDurationLabel.Text = _capsule.TotalDurationFormatted;
            SegmentCountLabel.Text = $"({_capsule.SegmentCount} segment{(_capsule.SegmentCount == 1 ? "" : "s")})";

            // Show/hide empty hint
            EmptyHintOverlay.Visibility = _capsule.HasSegments ? Visibility.Collapsed : Visibility.Visible;

            // Update move buttons
            UpdateMoveButtons();

            // Update schedule button
            ScheduleCapsuleButton.IsEnabled = _capsule.IsValid;

            // Notify change
            CapsuleChanged?.Invoke(this, _capsule);
        }

        private void UpdateMoveButtons()
        {
            var selectedIndex = SegmentsListBox.SelectedIndex;
            MoveUpButton.IsEnabled = selectedIndex > 0;
            MoveDownButton.IsEnabled = selectedIndex >= 0 && selectedIndex < _capsule.SegmentCount - 1;
        }

        private void UpdateWaveformPreview()
        {
            if (_capsule.HasSegments)
            {
                var segments = _capsule.GetSegmentAudioPaths();
                if (segments.Count > 0)
                {
                    CapsuleWaveformViewer.LoadCapsule(_capsule.Name ?? "Capsule", segments);
                }
            }
            else
            {
                CapsuleWaveformViewer.Clear();
            }
        }

        private string GetAudioPath(Commercial commercial)
        {
            if (_channel == null || commercial == null || string.IsNullOrEmpty(commercial.Filename))
                return null;

            var path = Path.Combine(_channel.PrimaryCommercialsPath ?? "", commercial.Filename);
            return File.Exists(path) ? path : null;
        }

        private void AddCommercialToCapsule(Commercial commercial)
        {
            if (commercial == null) return;

            var audioPath = GetAudioPath(commercial);
            _capsule.AddCommercial(commercial.Clone(), audioPath);
            _isDirty = true;

            // Auto-set capsule name if first segment
            if (_capsule.SegmentCount == 1 && string.IsNullOrWhiteSpace(CapsuleNameTextBox.Text))
            {
                CapsuleNameTextBox.Text = commercial.SanitizedSpotName;
                _capsule.Name = commercial.SanitizedSpotName;
            }

            UpdateUI();
            UpdateWaveformPreview();

            LogService.Info($"Added '{commercial.Spot}' to capsule");
        }

        #region Library Events

        private void LibrarySearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        private async void SearchDebounce_Tick(object sender, EventArgs e)
        {
            _searchDebounce.Stop();

            var searchText = LibrarySearchTextBox.Text;
            if (string.IsNullOrWhiteSpace(searchText))
            {
                LibraryListBox.ItemsSource = _libraryCommercials;
            }
            else
            {
                var filtered = await _commercialService.SearchCommercialsAsync(searchText);
                LibraryListBox.ItemsSource = filtered;
            }
        }

        private void LibraryListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LibraryListBox.SelectedItem is Commercial commercial)
            {
                AddCommercialToCapsule(commercial);
            }
        }

        private void LibraryListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void LibraryListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _isDragging)
                return;

            var position = e.GetPosition(null);
            var diff = _dragStartPoint - position;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (LibraryListBox.SelectedItem is Commercial commercial)
                {
                    _isDragging = true;
                    var data = new DataObject("Commercial", commercial);
                    DragDrop.DoDragDrop(LibraryListBox, data, DragDropEffects.Copy);
                    _isDragging = false;
                }
            }
        }

        #endregion

        #region Segments Panel Events

        private void SegmentsPanel_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("Commercial"))
            {
                e.Effects = DragDropEffects.Copy;
                DropOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void SegmentsPanel_DragLeave(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
        }

        private void SegmentsPanel_Drop(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;

            if (e.Data.GetDataPresent("Commercial"))
            {
                var commercial = e.Data.GetData("Commercial") as Commercial;
                if (commercial != null)
                {
                    AddCommercialToCapsule(commercial);
                }
            }
            e.Handled = true;
        }

        private void SegmentsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateMoveButtons();
        }

        private void SegmentsListBox_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("Commercial") || e.Data.GetDataPresent("CapsuleSegment"))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void SegmentsListBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("Commercial"))
            {
                var commercial = e.Data.GetData("Commercial") as Commercial;
                if (commercial != null)
                {
                    // Find drop position
                    var targetIndex = GetDropIndex(e);
                    var audioPath = GetAudioPath(commercial);
                    _capsule.InsertCommercial(targetIndex, commercial.Clone(), audioPath);
                    _isDirty = true;
                    UpdateUI();
                    UpdateWaveformPreview();
                }
            }
            else if (e.Data.GetDataPresent("CapsuleSegment"))
            {
                // Reordering
                var segment = e.Data.GetData("CapsuleSegment") as CapsuleSegment;
                if (segment != null)
                {
                    var fromIndex = _capsule.Segments.IndexOf(segment);
                    var toIndex = GetDropIndex(e);
                    if (fromIndex != toIndex)
                    {
                        _capsule.MoveSegment(fromIndex, toIndex);
                        _isDirty = true;
                        UpdateUI();
                        UpdateWaveformPreview();
                    }
                }
            }
            e.Handled = true;
        }

        private int GetDropIndex(DragEventArgs e)
        {
            var position = e.GetPosition(SegmentsListBox);
            for (int i = 0; i < SegmentsListBox.Items.Count; i++)
            {
                var item = SegmentsListBox.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                if (item != null)
                {
                    var itemPosition = item.TransformToAncestor(SegmentsListBox).Transform(new Point(0, 0));
                    if (position.Y < itemPosition.Y + item.ActualHeight / 2)
                    {
                        return i;
                    }
                }
            }
            return _capsule.SegmentCount;
        }

        private void SegmentsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void SegmentsListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _isDragging)
                return;

            var position = e.GetPosition(null);
            var diff = _dragStartPoint - position;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (SegmentsListBox.SelectedItem is CapsuleSegment segment)
                {
                    _isDragging = true;
                    var data = new DataObject("CapsuleSegment", segment);
                    DragDrop.DoDragDrop(SegmentsListBox, data, DragDropEffects.Move);
                    _isDragging = false;
                }
            }
        }

        private void RemoveSegmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is CapsuleSegment segment)
            {
                var index = _capsule.Segments.IndexOf(segment);
                if (index >= 0)
                {
                    _capsule.RemoveAt(index);
                    _isDirty = true;
                    UpdateUI();
                    UpdateWaveformPreview();
                    LogService.Info($"Removed segment '{segment.DisplayName}' from capsule");
                }
            }
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            var index = SegmentsListBox.SelectedIndex;
            if (index > 0)
            {
                _capsule.MoveSegment(index, index - 1);
                _isDirty = true;
                UpdateUI();
                SegmentsListBox.SelectedIndex = index - 1;
                UpdateWaveformPreview();
            }
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            var index = SegmentsListBox.SelectedIndex;
            if (index >= 0 && index < _capsule.SegmentCount - 1)
            {
                _capsule.MoveSegment(index, index + 1);
                _isDirty = true;
                UpdateUI();
                SegmentsListBox.SelectedIndex = index + 1;
                UpdateWaveformPreview();
            }
        }

        #endregion

        #region Header Controls

        private void CapsuleNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _capsule.Name = CapsuleNameTextBox.Text?.Trim();
            _isDirty = true;
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (_capsule.HasSegments)
            {
                var result = MessageBox.Show(
                    "Clear all segments from the capsule?",
                    "Clear Capsule",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _capsule.Clear();
                    CapsuleNameTextBox.Text = "";
                    _isDirty = false;
                    UpdateUI();
                    CapsuleWaveformViewer.Clear();
                    LogService.Info("Cleared capsule");
                }
            }
        }

        private void ScheduleCapsuleButton_Click(object sender, RoutedEventArgs e)
        {
            SetCapsuleReady();
        }

        #endregion

        /// <summary>
        /// Validate and get the built capsule
        /// </summary>
        public bool TryGetCapsule(out Capsule capsule)
        {
            capsule = null;

            if (!_capsule.HasSegments)
            {
                MessageBox.Show("Please add at least one commercial to the capsule.",
                    "Capsule Empty", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(_capsule.Name))
            {
                MessageBox.Show("Please enter a capsule name.",
                    "Name Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                CapsuleNameTextBox.Focus();
                return false;
            }

            capsule = _capsule;
            return true;
        }

        /// <summary>
        /// Set capsule as ready for scheduling
        /// </summary>
        public void SetCapsuleReady()
        {
            if (TryGetCapsule(out var capsule))
            {
                CapsuleReady?.Invoke(this, capsule);
            }
        }
    }
}
