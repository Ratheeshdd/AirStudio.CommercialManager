using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Core.Services.Audio
{
    /// <summary>
    /// Result of an audio operation
    /// </summary>
    public class AudioOperationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string OutputPath { get; set; }
        public TimeSpan Duration { get; set; }
        public List<ReplicationResult> ReplicationResults { get; set; } = new List<ReplicationResult>();

        public static AudioOperationResult Succeeded(string outputPath, TimeSpan duration)
        {
            return new AudioOperationResult { Success = true, OutputPath = outputPath, Duration = duration };
        }

        public static AudioOperationResult Failed(string message)
        {
            return new AudioOperationResult { Success = false, ErrorMessage = message };
        }
    }

    /// <summary>
    /// Result of replicating to a single target
    /// </summary>
    public class ReplicationResult
    {
        public string TargetPath { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Service for audio processing (conversion and replication)
    /// </summary>
    public class AudioService
    {
        // Target format: WAV PCM 16-bit 48000 Hz Stereo
        private const int TARGET_SAMPLE_RATE = 48000;
        private const int TARGET_BITS_PER_SAMPLE = 16;
        private const int TARGET_CHANNELS = 2;

        /// <summary>
        /// Convert an audio file to standard format (WAV PCM 16-bit 48000 Hz stereo)
        /// </summary>
        /// <param name="inputPath">Source audio file path</param>
        /// <param name="outputPath">Destination WAV file path</param>
        /// <returns>Operation result with duration</returns>
        public async Task<AudioOperationResult> ConvertToStandardFormatAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(inputPath))
            {
                return AudioOperationResult.Failed($"Input file not found: {inputPath}");
            }

            try
            {
                return await Task.Run(() =>
                {
                    // Ensure output directory exists
                    var outputDir = Path.GetDirectoryName(outputPath);
                    if (!Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    // Use temp file for atomic write
                    var tempPath = outputPath + ".tmp";

                    try
                    {
                        using (var reader = CreateAudioReader(inputPath))
                        {
                            var targetFormat = new WaveFormat(TARGET_SAMPLE_RATE, TARGET_BITS_PER_SAMPLE, TARGET_CHANNELS);

                            // Resample if needed
                            using (var resampler = new MediaFoundationResampler(reader, targetFormat))
                            {
                                resampler.ResamplerQuality = 60; // High quality

                                // Write to temp file
                                WaveFileWriter.CreateWaveFile(tempPath, resampler);
                            }
                        }

                        // Atomic replace
                        if (File.Exists(outputPath))
                        {
                            File.Delete(outputPath);
                        }
                        File.Move(tempPath, outputPath);

                        // Get duration
                        TimeSpan duration;
                        using (var reader = new WaveFileReader(outputPath))
                        {
                            duration = reader.TotalTime;
                        }

                        LogService.Info($"Converted audio: {inputPath} -> {outputPath} (Duration: {duration:hh\\:mm\\:ss})");
                        return AudioOperationResult.Succeeded(outputPath, duration);
                    }
                    finally
                    {
                        // Clean up temp file on failure
                        if (File.Exists(tempPath))
                        {
                            try { File.Delete(tempPath); } catch { }
                        }
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                LogService.Error($"Failed to convert audio: {inputPath}", ex);
                return AudioOperationResult.Failed(ex.Message);
            }
        }

        /// <summary>
        /// Convert and replicate audio to all X targets for a channel
        /// </summary>
        public async Task<AudioOperationResult> ConvertAndReplicateAsync(string inputPath, string spotName, Channel channel, CancellationToken cancellationToken = default)
        {
            if (channel == null || !channel.IsUsable)
            {
                return AudioOperationResult.Failed("Channel is not configured with X targets");
            }

            // First convert to primary target
            var primaryTarget = channel.PrimaryXRoot;
            var primaryPath = Path.Combine(channel.GetCommercialsPath(primaryTarget), $"{spotName}.WAV");

            var conversionResult = await ConvertToStandardFormatAsync(inputPath, primaryPath, cancellationToken);
            if (!conversionResult.Success)
            {
                return conversionResult;
            }

            // Replicate to other targets
            var results = new List<ReplicationResult>();
            results.Add(new ReplicationResult { TargetPath = primaryPath, Success = true });

            foreach (var xRoot in channel.XRootTargets.Skip(1))
            {
                var targetPath = Path.Combine(channel.GetCommercialsPath(xRoot), $"{spotName}.WAV");
                var replicationResult = await ReplicateFileAsync(primaryPath, targetPath, cancellationToken);
                results.Add(replicationResult);
            }

            conversionResult.ReplicationResults = results;

            var failedCount = results.FindAll(r => !r.Success).Count;
            if (failedCount > 0)
            {
                LogService.Warning($"Audio replicated with {failedCount} failures out of {results.Count} targets");
            }

            return conversionResult;
        }

        /// <summary>
        /// Replicate a file to a target location
        /// </summary>
        public async Task<ReplicationResult> ReplicateFileAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default)
        {
            try
            {
                return await Task.Run(() =>
                {
                    // Ensure target directory exists
                    var targetDir = Path.GetDirectoryName(targetPath);
                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    // Use temp file for atomic write
                    var tempPath = targetPath + ".tmp";

                    try
                    {
                        File.Copy(sourcePath, tempPath, overwrite: true);

                        if (File.Exists(targetPath))
                        {
                            File.Delete(targetPath);
                        }
                        File.Move(tempPath, targetPath);

                        LogService.Info($"Replicated: {sourcePath} -> {targetPath}");
                        return new ReplicationResult { TargetPath = targetPath, Success = true };
                    }
                    finally
                    {
                        if (File.Exists(tempPath))
                        {
                            try { File.Delete(tempPath); } catch { }
                        }
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                LogService.Error($"Failed to replicate to {targetPath}", ex);
                return new ReplicationResult { TargetPath = targetPath, Success = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// Replicate an existing audio file to all X targets
        /// </summary>
        public async Task<List<ReplicationResult>> ReplicateToAllTargetsAsync(string sourcePath, Channel channel, CancellationToken cancellationToken = default)
        {
            var results = new List<ReplicationResult>();
            var filename = Path.GetFileName(sourcePath);

            foreach (var xRoot in channel.XRootTargets)
            {
                var targetPath = Path.Combine(channel.GetCommercialsPath(xRoot), filename);

                if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new ReplicationResult { TargetPath = targetPath, Success = true });
                    continue;
                }

                var result = await ReplicateFileAsync(sourcePath, targetPath, cancellationToken);
                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// Get audio duration
        /// </summary>
        public TimeSpan GetAudioDuration(string audioPath)
        {
            try
            {
                using (var reader = CreateAudioReader(audioPath))
                {
                    return reader.TotalTime;
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"Failed to get audio duration: {audioPath}", ex);
                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Format duration as HH:mm:ss
        /// </summary>
        public static string FormatDuration(TimeSpan duration)
        {
            return duration.ToString(@"hh\:mm\:ss");
        }

        /// <summary>
        /// Create appropriate reader for the audio file format
        /// </summary>
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
                    // Try MediaFoundation for other formats
                    return new MediaFoundationReader(filePath);
            }
        }

        /// <summary>
        /// Check if a file is a supported audio format
        /// </summary>
        public static bool IsSupportedAudioFormat(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension == ".wav" || extension == ".mp3" ||
                   extension == ".aiff" || extension == ".aif" ||
                   extension == ".m4a" || extension == ".wma" ||
                   extension == ".ogg" || extension == ".flac";
        }
    }
}
