using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AirStudio.CommercialManager.Core.Services.Audio;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Controls
{
    /// <summary>
    /// Waveform visualization control with playback support
    /// </summary>
    public partial class WaveformViewer : UserControl, IDisposable
    {
        private readonly WaveformGenerator _waveformGenerator = new WaveformGenerator();
        private readonly AudioPlayer _audioPlayer = new AudioPlayer();
        private readonly DispatcherTimer _positionTimer;

        private WaveformData _waveformData;
        private CapsuleWaveformData _capsuleData;
        private bool _disposed;

        // Theme colors for waveform
        private static readonly Color[] SegmentColors = new[]
        {
            Color.FromRgb(90, 143, 196),   // Blue
            Color.FromRgb(143, 196, 90),   // Green
            Color.FromRgb(196, 143, 90),   // Orange
            Color.FromRgb(143, 90, 196),   // Purple
            Color.FromRgb(196, 90, 143),   // Pink
            Color.FromRgb(90, 196, 143)    // Teal
        };

        private static readonly Color DefaultWaveformColor = Color.FromRgb(90, 143, 196);

        public WaveformViewer()
        {
            InitializeComponent();

            // Setup position update timer
            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _positionTimer.Tick += PositionTimer_Tick;

            // Setup audio player events
            _audioPlayer.StateChanged += AudioPlayer_StateChanged;
            _audioPlayer.PlaybackCompleted += AudioPlayer_PlaybackCompleted;

            // Handle resize
            SizeChanged += WaveformViewer_SizeChanged;
        }

        /// <summary>
        /// Load and display waveform for a single audio file
        /// </summary>
        public async void LoadAudio(string audioPath)
        {
            if (string.IsNullOrEmpty(audioPath))
            {
                Clear();
                return;
            }

            try
            {
                EmptyOverlay.Visibility = Visibility.Collapsed;
                LoadingOverlay.Visibility = Visibility.Visible;

                // Load audio in player
                if (!_audioPlayer.Load(audioPath))
                {
                    ShowError("Failed to load audio");
                    return;
                }

                // Generate waveform
                _waveformData = await _waveformGenerator.GenerateWaveformAsync(audioPath);
                _capsuleData = null;

                if (_waveformData == null)
                {
                    ShowError("Failed to generate waveform");
                    return;
                }

                // Update UI
                LoadingOverlay.Visibility = Visibility.Collapsed;
                SegmentInfo.Visibility = Visibility.Collapsed;
                EnableControls(true);

                UpdateTimeDisplay();
                RenderWaveform();
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load audio in viewer", ex);
                ShowError($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Load and display waveform for multiple audio files (creates capsule-like display)
        /// </summary>
        public async void LoadMultipleAudio(List<string> audioPaths)
        {
            if (audioPaths == null || audioPaths.Count == 0)
            {
                Clear();
                return;
            }

            // Convert to segment format and use LoadCapsule
            var segments = new List<(string name, string filePath)>();
            for (int i = 0; i < audioPaths.Count; i++)
            {
                var fileName = System.IO.Path.GetFileNameWithoutExtension(audioPaths[i]);
                segments.Add((fileName, audioPaths[i]));
            }

            LoadCapsule("Playlist", segments);
        }

        /// <summary>
        /// Start playback
        /// </summary>
        public void Play()
        {
            if (_audioPlayer != null && (_waveformData != null || _capsuleData != null))
            {
                _audioPlayer.Play();
            }
        }

        /// <summary>
        /// Stop playback
        /// </summary>
        public void Stop()
        {
            _audioPlayer?.Stop();
            CurrentTimeLabel.Text = "00:00:00";
            UpdatePlayheadPosition();
        }

        /// <summary>
        /// Gets the current playback position
        /// </summary>
        public TimeSpan Position => _audioPlayer?.Position ?? TimeSpan.Zero;

        /// <summary>
        /// Gets the total duration
        /// </summary>
        public TimeSpan Duration => _waveformData?.Duration ?? _capsuleData?.TotalDuration ?? TimeSpan.Zero;

        /// <summary>
        /// Gets whether audio is loaded
        /// </summary>
        public bool HasAudio => _waveformData != null || _capsuleData != null;

        /// <summary>
        /// Load and display waveform for multiple segments (capsule)
        /// </summary>
        public async void LoadCapsule(string capsuleName, List<(string name, string filePath)> segments)
        {
            if (segments == null || segments.Count == 0)
            {
                Clear();
                return;
            }

            try
            {
                EmptyOverlay.Visibility = Visibility.Collapsed;
                LoadingOverlay.Visibility = Visibility.Visible;

                // Generate capsule waveform
                _capsuleData = await _waveformGenerator.GenerateCapsuleWaveformAsync(capsuleName, segments);
                _waveformData = null;

                if (_capsuleData == null || _capsuleData.Segments.Count == 0)
                {
                    ShowError("Failed to generate capsule waveform");
                    return;
                }

                // Load first segment for playback
                var firstSegment = _capsuleData.Segments[0];
                _audioPlayer.Load(firstSegment.FilePath);

                // Update UI
                LoadingOverlay.Visibility = Visibility.Collapsed;
                SegmentInfo.Visibility = Visibility.Visible;
                SegmentLabel.Text = $"Segment: {firstSegment.Name}";
                EnableControls(true);

                TotalTimeLabel.Text = FormatTime(_capsuleData.TotalDuration);
                CurrentTimeLabel.Text = "00:00:00";

                RenderCapsuleWaveform();
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load capsule in viewer", ex);
                ShowError($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear the viewer
        /// </summary>
        public void Clear()
        {
            _audioPlayer.Stop();
            _waveformData = null;
            _capsuleData = null;

            WaveformCanvas.Children.Clear();
            EmptyOverlay.Visibility = Visibility.Visible;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            SegmentInfo.Visibility = Visibility.Collapsed;
            Playhead.Visibility = Visibility.Collapsed;

            CurrentTimeLabel.Text = "00:00:00";
            TotalTimeLabel.Text = "00:00:00";

            EnableControls(false);
        }

        private void ShowError(string message)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            EmptyOverlay.Visibility = Visibility.Visible;
            // Could show error message here
        }

        private void EnableControls(bool enabled)
        {
            // Controls have been moved to MainWindow - just show/hide time overlay
            TimeOverlayBorder.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateTimeDisplay()
        {
            if (_waveformData != null)
            {
                TotalTimeLabel.Text = FormatTime(_waveformData.Duration);
                CurrentTimeLabel.Text = FormatTime(_audioPlayer.Position);
            }
        }

        private string FormatTime(TimeSpan time)
        {
            return time.ToString(@"hh\:mm\:ss");
        }

        #region Waveform Rendering

        private void RenderWaveform()
        {
            if (_waveformData == null || _waveformData.Peaks.Count == 0)
                return;

            WaveformCanvas.Children.Clear();

            var width = WaveformCanvas.ActualWidth;
            var height = WaveformCanvas.ActualHeight;

            if (width <= 0 || height <= 0)
                return;

            var peaks = _waveformData.Peaks;
            var centerY = height / 2;
            var peakWidth = width / peaks.Count;

            var brush = new SolidColorBrush(DefaultWaveformColor);
            var geometry = new StreamGeometry();

            using (var ctx = geometry.Open())
            {
                // Draw waveform as filled area
                ctx.BeginFigure(new Point(0, centerY), true, true);

                // Upper half
                for (int i = 0; i < peaks.Count; i++)
                {
                    var x = i * peakWidth;
                    var y = centerY - (peaks[i].Max * centerY * 0.9);
                    ctx.LineTo(new Point(x, y), true, false);
                }

                // Lower half (reverse)
                for (int i = peaks.Count - 1; i >= 0; i--)
                {
                    var x = i * peakWidth;
                    var y = centerY - (peaks[i].Min * centerY * 0.9);
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }

            geometry.Freeze();

            var path = new Path
            {
                Data = geometry,
                Fill = brush,
                Opacity = 0.8
            };

            WaveformCanvas.Children.Add(path);

            // Draw center line
            var centerLine = new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = centerY,
                Y2 = centerY,
                Stroke = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                StrokeThickness = 1
            };
            WaveformCanvas.Children.Add(centerLine);

            Playhead.Visibility = Visibility.Visible;
        }

        private void RenderCapsuleWaveform()
        {
            if (_capsuleData == null || _capsuleData.Segments.Count == 0)
                return;

            WaveformCanvas.Children.Clear();

            var width = WaveformCanvas.ActualWidth;
            var height = WaveformCanvas.ActualHeight;

            if (width <= 0 || height <= 0)
                return;

            var centerY = height / 2;
            var totalDuration = _capsuleData.TotalDuration.TotalSeconds;

            foreach (var segment in _capsuleData.Segments)
            {
                if (segment.Waveform == null || segment.Waveform.Peaks.Count == 0)
                    continue;

                var startX = (segment.StartTime.TotalSeconds / totalDuration) * width;
                var segmentWidth = (segment.Duration.TotalSeconds / totalDuration) * width;
                var peaks = segment.Waveform.Peaks;
                var peakWidth = segmentWidth / peaks.Count;

                var color = SegmentColors[segment.ColorIndex % SegmentColors.Length];
                var brush = new SolidColorBrush(color);

                var geometry = new StreamGeometry();

                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(startX, centerY), true, true);

                    // Upper half
                    for (int i = 0; i < peaks.Count; i++)
                    {
                        var x = startX + (i * peakWidth);
                        var y = centerY - (peaks[i].Max * centerY * 0.85);
                        ctx.LineTo(new Point(x, y), true, false);
                    }

                    // Lower half
                    for (int i = peaks.Count - 1; i >= 0; i--)
                    {
                        var x = startX + (i * peakWidth);
                        var y = centerY - (peaks[i].Min * centerY * 0.85);
                        ctx.LineTo(new Point(x, y), true, false);
                    }
                }

                geometry.Freeze();

                var path = new Path
                {
                    Data = geometry,
                    Fill = brush,
                    Opacity = 0.8
                };

                WaveformCanvas.Children.Add(path);

                // Draw segment separator
                if (segment.StartTime > TimeSpan.Zero)
                {
                    var separator = new Line
                    {
                        X1 = startX,
                        X2 = startX,
                        Y1 = 0,
                        Y2 = height,
                        Stroke = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 4, 2 }
                    };
                    WaveformCanvas.Children.Add(separator);
                }

                // Draw segment label
                var label = new TextBlock
                {
                    Text = segment.Name,
                    Foreground = new SolidColorBrush(Colors.White),
                    FontSize = 10,
                    Opacity = 0.7
                };
                Canvas.SetLeft(label, startX + 4);
                Canvas.SetTop(label, 4);
                WaveformCanvas.Children.Add(label);
            }

            // Draw center line
            var centerLine = new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = centerY,
                Y2 = centerY,
                Stroke = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                StrokeThickness = 1
            };
            WaveformCanvas.Children.Add(centerLine);

            Playhead.Visibility = Visibility.Visible;
        }

        private void UpdatePlayheadPosition()
        {
            if (_waveformData == null && _capsuleData == null)
                return;

            var width = WaveformCanvas.ActualWidth;
            var duration = _waveformData?.Duration ?? _capsuleData?.TotalDuration ?? TimeSpan.Zero;

            if (width <= 0 || duration.TotalSeconds <= 0)
                return;

            var position = _audioPlayer.Position;
            var x = (position.TotalSeconds / duration.TotalSeconds) * width;

            Playhead.X1 = x;
            Playhead.X2 = x;
        }

        #endregion

        #region Event Handlers

        private void WaveformViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_capsuleData != null)
            {
                RenderCapsuleWaveform();
            }
            else if (_waveformData != null)
            {
                RenderWaveform();
            }
        }

        private void WaveformArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_waveformData == null && _capsuleData == null)
                return;

            var width = WaveformCanvas.ActualWidth;
            if (width <= 0)
                return;

            var position = e.GetPosition(WaveformCanvas);
            var percent = position.X / width;
            _audioPlayer.SeekPercent(percent);

            // Update current time display
            CurrentTimeLabel.Text = FormatTime(_audioPlayer.Position);
            UpdatePlayheadPosition();
        }

        private void AudioPlayer_StateChanged(object sender, PlaybackState state)
        {
            Dispatcher.Invoke(() =>
            {
                switch (state)
                {
                    case PlaybackState.Playing:
                        _positionTimer.Start();
                        break;
                    case PlaybackState.Paused:
                    case PlaybackState.Stopped:
                        _positionTimer.Stop();
                        break;
                }

                // Notify MainWindow about state change via event
                StateChanged?.Invoke(this, state);
            });
        }

        /// <summary>
        /// Event fired when playback state changes
        /// </summary>
        public event EventHandler<PlaybackState> StateChanged;

        private void AudioPlayer_PlaybackCompleted(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                CurrentTimeLabel.Text = "00:00:00";
                UpdatePlayheadPosition();

                // Notify MainWindow
                PlaybackCompleted?.Invoke(this, EventArgs.Empty);
            });
        }

        /// <summary>
        /// Event fired when playback completes
        /// </summary>
        public event EventHandler PlaybackCompleted;

        private void PositionTimer_Tick(object sender, EventArgs e)
        {
            CurrentTimeLabel.Text = FormatTime(_audioPlayer.Position);
            UpdatePlayheadPosition();

            // Update segment info for capsule
            if (_capsuleData != null)
            {
                var segment = _capsuleData.GetSegmentAtTime(_audioPlayer.Position);
                if (segment != null)
                {
                    SegmentLabel.Text = $"Segment: {segment.Name}";
                }
            }

            // Notify MainWindow about position change
            PositionChanged?.Invoke(this, _audioPlayer.Position);
        }

        /// <summary>
        /// Event fired when playback position changes
        /// </summary>
        public event EventHandler<TimeSpan> PositionChanged;

        #endregion

        public void Dispose()
        {
            if (_disposed)
                return;

            _positionTimer.Stop();
            _audioPlayer.Dispose();
            _disposed = true;
        }
    }
}
