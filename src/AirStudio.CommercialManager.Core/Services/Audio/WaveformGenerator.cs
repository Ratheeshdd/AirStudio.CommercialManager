using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Core.Services.Audio
{
    /// <summary>
    /// Generates waveform peak data from audio files
    /// </summary>
    public class WaveformGenerator
    {
        // Target peaks per second for visualization
        private const int PEAKS_PER_SECOND = 100;

        // Cache for generated waveforms
        private static readonly ConcurrentDictionary<string, WaveformData> _cache =
            new ConcurrentDictionary<string, WaveformData>(StringComparer.OrdinalIgnoreCase);

        // Cache expiry time
        private static readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Generate waveform data for an audio file
        /// </summary>
        /// <param name="audioPath">Path to audio file</param>
        /// <param name="useCache">Whether to use cached data if available</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Waveform data</returns>
        public async Task<WaveformData> GenerateWaveformAsync(string audioPath, bool useCache = true, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
            {
                LogService.Warning($"Waveform generation: file not found - {audioPath}");
                return null;
            }

            // Check cache
            if (useCache && _cache.TryGetValue(audioPath, out var cached))
            {
                if (DateTime.Now - cached.GeneratedAt < _cacheExpiry)
                {
                    return cached;
                }
                _cache.TryRemove(audioPath, out _);
            }

            try
            {
                var waveform = await Task.Run(() => GenerateWaveformInternal(audioPath), cancellationToken);

                if (waveform != null && useCache)
                {
                    _cache[audioPath] = waveform;
                }

                return waveform;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                LogService.Error($"Failed to generate waveform for {audioPath}", ex);
                return null;
            }
        }

        private WaveformData GenerateWaveformInternal(string audioPath)
        {
            using (var reader = CreateAudioReader(audioPath))
            {
                var waveFormat = reader.WaveFormat;
                var totalSamples = reader.Length / (waveFormat.BitsPerSample / 8) / waveFormat.Channels;
                var duration = reader.TotalTime;

                // Calculate samples per peak
                var targetPeakCount = (int)(duration.TotalSeconds * PEAKS_PER_SECOND);
                targetPeakCount = Math.Max(100, targetPeakCount); // Minimum 100 peaks

                var samplesPerPeak = (int)Math.Ceiling((double)totalSamples / targetPeakCount);
                samplesPerPeak = Math.Max(1, samplesPerPeak);

                var peaks = new List<WaveformPeak>(targetPeakCount);

                // Read and process samples
                var buffer = new float[samplesPerPeak * waveFormat.Channels];
                var provider = reader.ToSampleProvider();

                int samplesRead;
                while ((samplesRead = provider.Read(buffer, 0, buffer.Length)) > 0)
                {
                    float min = 0, max = 0;

                    // Process all channels, find overall min/max
                    for (int i = 0; i < samplesRead; i++)
                    {
                        var sample = buffer[i];
                        if (sample < min) min = sample;
                        if (sample > max) max = sample;
                    }

                    peaks.Add(new WaveformPeak(min, max));
                }

                LogService.Info($"Generated waveform: {audioPath} ({peaks.Count} peaks, {duration:mm\\:ss})");

                return new WaveformData
                {
                    FilePath = audioPath,
                    Duration = duration,
                    SampleRate = waveFormat.SampleRate,
                    Channels = waveFormat.Channels,
                    Peaks = peaks,
                    SamplesPerPeak = samplesPerPeak,
                    GeneratedAt = DateTime.Now
                };
            }
        }

        /// <summary>
        /// Generate waveform data for multiple segments (capsule)
        /// </summary>
        public async Task<CapsuleWaveformData> GenerateCapsuleWaveformAsync(
            string capsuleName,
            List<(string name, string filePath)> segments,
            CancellationToken cancellationToken = default)
        {
            var capsule = new CapsuleWaveformData
            {
                CapsuleName = capsuleName,
                Segments = new List<WaveformSegment>()
            };

            var currentTime = TimeSpan.Zero;
            int colorIndex = 0;

            foreach (var (name, filePath) in segments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var waveform = await GenerateWaveformAsync(filePath, useCache: true, cancellationToken);

                if (waveform != null)
                {
                    capsule.Segments.Add(new WaveformSegment
                    {
                        Name = name,
                        FilePath = filePath,
                        StartTime = currentTime,
                        Duration = waveform.Duration,
                        Waveform = waveform,
                        ColorIndex = colorIndex % 6 // Cycle through 6 colors
                    });

                    currentTime += waveform.Duration;
                    colorIndex++;
                }
            }

            capsule.TotalDuration = currentTime;
            return capsule;
        }

        /// <summary>
        /// Clear the waveform cache
        /// </summary>
        public static void ClearCache()
        {
            _cache.Clear();
            LogService.Info("Waveform cache cleared");
        }

        /// <summary>
        /// Remove a specific file from cache
        /// </summary>
        public static void InvalidateCache(string audioPath)
        {
            _cache.TryRemove(audioPath, out _);
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
    }
}
