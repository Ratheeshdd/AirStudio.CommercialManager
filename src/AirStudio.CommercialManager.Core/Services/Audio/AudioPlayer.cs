using System;
using System.IO;
using NAudio.Wave;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Core.Services.Audio
{
    /// <summary>
    /// Playback state
    /// </summary>
    public enum PlaybackState
    {
        Stopped,
        Playing,
        Paused
    }

    /// <summary>
    /// Event args for playback position changes
    /// </summary>
    public class PlaybackPositionEventArgs : EventArgs
    {
        public TimeSpan Position { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Audio playback using NAudio
    /// </summary>
    public class AudioPlayer : IDisposable
    {
        private WaveOutEvent _waveOut;
        private WaveStream _audioReader;
        private string _currentFile;
        private bool _disposed;

        /// <summary>
        /// Current playback state
        /// </summary>
        public PlaybackState State { get; private set; } = PlaybackState.Stopped;

        /// <summary>
        /// Current playback position
        /// </summary>
        public TimeSpan Position
        {
            get => _audioReader?.CurrentTime ?? TimeSpan.Zero;
            set
            {
                if (_audioReader != null && value >= TimeSpan.Zero && value <= Duration)
                {
                    _audioReader.CurrentTime = value;
                    PositionChanged?.Invoke(this, new PlaybackPositionEventArgs
                    {
                        Position = value,
                        Duration = Duration
                    });
                }
            }
        }

        /// <summary>
        /// Total duration of current audio
        /// </summary>
        public TimeSpan Duration => _audioReader?.TotalTime ?? TimeSpan.Zero;

        /// <summary>
        /// Current audio file path
        /// </summary>
        public string CurrentFile => _currentFile;

        /// <summary>
        /// Volume (0.0 to 1.0)
        /// </summary>
        public float Volume
        {
            get => _waveOut?.Volume ?? 1.0f;
            set
            {
                if (_waveOut != null)
                {
                    _waveOut.Volume = Math.Max(0, Math.Min(1, value));
                }
            }
        }

        /// <summary>
        /// Event raised when playback state changes
        /// </summary>
        public event EventHandler<PlaybackState> StateChanged;

        /// <summary>
        /// Event raised when playback position changes
        /// </summary>
        public event EventHandler<PlaybackPositionEventArgs> PositionChanged;

        /// <summary>
        /// Event raised when playback completes
        /// </summary>
        public event EventHandler PlaybackCompleted;

        /// <summary>
        /// Load an audio file for playback
        /// </summary>
        public bool Load(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                LogService.Warning($"AudioPlayer: file not found - {filePath}");
                return false;
            }

            try
            {
                Stop();
                DisposeResources();

                _audioReader = CreateAudioReader(filePath);
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_audioReader);
                _waveOut.PlaybackStopped += OnPlaybackStopped;

                _currentFile = filePath;
                State = PlaybackState.Stopped;

                LogService.Info($"AudioPlayer: loaded {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Error($"AudioPlayer: failed to load {filePath}", ex);
                DisposeResources();
                return false;
            }
        }

        /// <summary>
        /// Start or resume playback
        /// </summary>
        public void Play()
        {
            if (_waveOut == null || _audioReader == null)
                return;

            try
            {
                if (State == PlaybackState.Stopped)
                {
                    _audioReader.Position = 0;
                }

                _waveOut.Play();
                State = PlaybackState.Playing;
                StateChanged?.Invoke(this, State);

                LogService.Info($"AudioPlayer: playing");
            }
            catch (Exception ex)
            {
                LogService.Error("AudioPlayer: play failed", ex);
            }
        }

        /// <summary>
        /// Pause playback
        /// </summary>
        public void Pause()
        {
            if (_waveOut == null || State != PlaybackState.Playing)
                return;

            try
            {
                _waveOut.Pause();
                State = PlaybackState.Paused;
                StateChanged?.Invoke(this, State);

                LogService.Info($"AudioPlayer: paused at {Position:mm\\:ss}");
            }
            catch (Exception ex)
            {
                LogService.Error("AudioPlayer: pause failed", ex);
            }
        }

        /// <summary>
        /// Stop playback
        /// </summary>
        public void Stop()
        {
            if (_waveOut == null)
                return;

            try
            {
                _waveOut.Stop();
                if (_audioReader != null)
                {
                    _audioReader.Position = 0;
                }
                State = PlaybackState.Stopped;
                StateChanged?.Invoke(this, State);

                LogService.Info("AudioPlayer: stopped");
            }
            catch (Exception ex)
            {
                LogService.Error("AudioPlayer: stop failed", ex);
            }
        }

        /// <summary>
        /// Toggle play/pause
        /// </summary>
        public void TogglePlayPause()
        {
            if (State == PlaybackState.Playing)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        /// <summary>
        /// Seek to a specific position
        /// </summary>
        public void Seek(TimeSpan position)
        {
            Position = position;
        }

        /// <summary>
        /// Seek by percentage (0.0 to 1.0)
        /// </summary>
        public void SeekPercent(double percent)
        {
            if (_audioReader == null)
                return;

            percent = Math.Max(0, Math.Min(1, percent));
            var position = TimeSpan.FromSeconds(Duration.TotalSeconds * percent);
            Position = position;
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                LogService.Error("AudioPlayer: playback error", e.Exception);
            }

            // Check if we reached the end
            if (_audioReader != null && _audioReader.Position >= _audioReader.Length)
            {
                State = PlaybackState.Stopped;
                StateChanged?.Invoke(this, State);
                PlaybackCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        private WaveStream CreateAudioReader(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            switch (extension)
            {
                case ".wav":
                    return new WaveFileReader(filePath);
                case ".mp3":
                    return new Mp3FileReader(filePath);
                case ".aiff":
                case ".aif":
                    return new AiffFileReader(filePath);
                default:
                    return new MediaFoundationReader(filePath);
            }
        }

        private void DisposeResources()
        {
            if (_waveOut != null)
            {
                _waveOut.PlaybackStopped -= OnPlaybackStopped;
                _waveOut.Dispose();
                _waveOut = null;
            }

            if (_audioReader != null)
            {
                _audioReader.Dispose();
                _audioReader = null;
            }

            _currentFile = null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Stop();
            DisposeResources();
            _disposed = true;
        }
    }
}
