using System;
using System.Collections.Generic;

namespace AirStudio.CommercialManager.Core.Services.Audio
{
    /// <summary>
    /// Represents waveform peak data for visualization
    /// </summary>
    public class WaveformData
    {
        /// <summary>
        /// Source audio file path
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Total duration of the audio
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Sample rate of the source audio
        /// </summary>
        public int SampleRate { get; set; }

        /// <summary>
        /// Number of channels
        /// </summary>
        public int Channels { get; set; }

        /// <summary>
        /// Peak data points (normalized -1 to 1)
        /// Each point contains min and max for that sample range
        /// </summary>
        public List<WaveformPeak> Peaks { get; set; } = new List<WaveformPeak>();

        /// <summary>
        /// Number of samples per peak (downsampling factor)
        /// </summary>
        public int SamplesPerPeak { get; set; }

        /// <summary>
        /// When this waveform data was generated
        /// </summary>
        public DateTime GeneratedAt { get; set; }

        /// <summary>
        /// Get peak at a specific time position
        /// </summary>
        public WaveformPeak GetPeakAtTime(TimeSpan time)
        {
            if (Peaks == null || Peaks.Count == 0 || Duration.TotalSeconds == 0)
                return new WaveformPeak();

            var ratio = time.TotalSeconds / Duration.TotalSeconds;
            var index = (int)(ratio * Peaks.Count);
            index = Math.Max(0, Math.Min(index, Peaks.Count - 1));
            return Peaks[index];
        }

        /// <summary>
        /// Get peaks for a time range
        /// </summary>
        public List<WaveformPeak> GetPeaksInRange(TimeSpan start, TimeSpan end)
        {
            if (Peaks == null || Peaks.Count == 0 || Duration.TotalSeconds == 0)
                return new List<WaveformPeak>();

            var startRatio = start.TotalSeconds / Duration.TotalSeconds;
            var endRatio = end.TotalSeconds / Duration.TotalSeconds;

            var startIndex = (int)(startRatio * Peaks.Count);
            var endIndex = (int)(endRatio * Peaks.Count);

            startIndex = Math.Max(0, startIndex);
            endIndex = Math.Min(Peaks.Count, endIndex);

            if (startIndex >= endIndex)
                return new List<WaveformPeak>();

            return Peaks.GetRange(startIndex, endIndex - startIndex);
        }
    }

    /// <summary>
    /// Single peak point in the waveform (min/max for a sample range)
    /// </summary>
    public struct WaveformPeak
    {
        /// <summary>
        /// Minimum sample value in this range (normalized -1 to 1)
        /// </summary>
        public float Min { get; set; }

        /// <summary>
        /// Maximum sample value in this range (normalized -1 to 1)
        /// </summary>
        public float Max { get; set; }

        /// <summary>
        /// Get the amplitude (absolute max)
        /// </summary>
        public float Amplitude => Math.Max(Math.Abs(Min), Math.Abs(Max));

        public WaveformPeak(float min, float max)
        {
            Min = min;
            Max = max;
        }
    }

    /// <summary>
    /// Represents a segment in a capsule waveform
    /// </summary>
    public class WaveformSegment
    {
        /// <summary>
        /// Segment identifier (spot name)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Start time within the capsule
        /// </summary>
        public TimeSpan StartTime { get; set; }

        /// <summary>
        /// Duration of this segment
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// End time (computed)
        /// </summary>
        public TimeSpan EndTime => StartTime + Duration;

        /// <summary>
        /// Waveform data for this segment
        /// </summary>
        public WaveformData Waveform { get; set; }

        /// <summary>
        /// Color index for rendering (0-based)
        /// </summary>
        public int ColorIndex { get; set; }

        /// <summary>
        /// Audio file path
        /// </summary>
        public string FilePath { get; set; }
    }

    /// <summary>
    /// Waveform data for a complete capsule (multiple segments)
    /// </summary>
    public class CapsuleWaveformData
    {
        /// <summary>
        /// Capsule name
        /// </summary>
        public string CapsuleName { get; set; }

        /// <summary>
        /// Total duration of all segments
        /// </summary>
        public TimeSpan TotalDuration { get; set; }

        /// <summary>
        /// Individual segments
        /// </summary>
        public List<WaveformSegment> Segments { get; set; } = new List<WaveformSegment>();

        /// <summary>
        /// Get segment at a specific time position
        /// </summary>
        public WaveformSegment GetSegmentAtTime(TimeSpan time)
        {
            foreach (var segment in Segments)
            {
                if (time >= segment.StartTime && time < segment.EndTime)
                    return segment;
            }
            return Segments.Count > 0 ? Segments[Segments.Count - 1] : null;
        }
    }
}
