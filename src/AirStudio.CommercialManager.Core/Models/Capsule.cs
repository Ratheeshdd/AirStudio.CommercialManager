using System;
using System.Collections.Generic;
using System.Linq;

namespace AirStudio.CommercialManager.Core.Models
{
    /// <summary>
    /// Represents a segment within a capsule
    /// </summary>
    public class CapsuleSegment
    {
        /// <summary>
        /// Order index within the capsule (1-based)
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Commercial data
        /// </summary>
        public Commercial Commercial { get; set; }

        /// <summary>
        /// Full path to the audio file
        /// </summary>
        public string AudioPath { get; set; }

        /// <summary>
        /// Duration of this segment
        /// </summary>
        public TimeSpan Duration => Commercial?.DurationTimeSpan ?? TimeSpan.Zero;

        /// <summary>
        /// Display name for this segment
        /// </summary>
        public string DisplayName => Commercial?.Spot ?? $"Segment {Order}";
    }

    /// <summary>
    /// Represents a capsule (collection of commercials scheduled together)
    /// </summary>
    public class Capsule
    {
        /// <summary>
        /// Capsule name (used for filename and Programme field in TAG)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// List of segments in order
        /// </summary>
        public List<CapsuleSegment> Segments { get; set; } = new List<CapsuleSegment>();

        /// <summary>
        /// Total duration of all segments
        /// </summary>
        public TimeSpan TotalDuration =>
            TimeSpan.FromSeconds(Segments.Sum(s => s.Duration.TotalSeconds));

        /// <summary>
        /// Total duration formatted as HH:mm:ss
        /// </summary>
        public string TotalDurationFormatted => TotalDuration.ToString(@"hh\:mm\:ss");

        /// <summary>
        /// Number of segments
        /// </summary>
        public int SegmentCount => Segments.Count;

        /// <summary>
        /// Check if capsule has any segments
        /// </summary>
        public bool HasSegments => Segments.Count > 0;

        /// <summary>
        /// Check if this is a single-commercial capsule
        /// </summary>
        public bool IsSingleCommercial => Segments.Count == 1;

        /// <summary>
        /// Validate the capsule
        /// </summary>
        public bool IsValid =>
            !string.IsNullOrWhiteSpace(Name) &&
            Segments.Count > 0 &&
            Segments.All(s => s.Commercial != null);

        /// <summary>
        /// Add a commercial to the capsule
        /// </summary>
        public void AddCommercial(Commercial commercial, string audioPath)
        {
            var segment = new CapsuleSegment
            {
                Order = Segments.Count + 1,
                Commercial = commercial,
                AudioPath = audioPath
            };
            Segments.Add(segment);
        }

        /// <summary>
        /// Insert a commercial at a specific position
        /// </summary>
        public void InsertCommercial(int index, Commercial commercial, string audioPath)
        {
            var segment = new CapsuleSegment
            {
                Order = index + 1,
                Commercial = commercial,
                AudioPath = audioPath
            };
            Segments.Insert(index, segment);
            ReorderSegments();
        }

        /// <summary>
        /// Remove a segment by index
        /// </summary>
        public void RemoveAt(int index)
        {
            if (index >= 0 && index < Segments.Count)
            {
                Segments.RemoveAt(index);
                ReorderSegments();
            }
        }

        /// <summary>
        /// Move a segment from one position to another
        /// </summary>
        public void MoveSegment(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= Segments.Count ||
                toIndex < 0 || toIndex >= Segments.Count ||
                fromIndex == toIndex)
                return;

            var segment = Segments[fromIndex];
            Segments.RemoveAt(fromIndex);
            Segments.Insert(toIndex, segment);
            ReorderSegments();
        }

        /// <summary>
        /// Clear all segments
        /// </summary>
        public void Clear()
        {
            Segments.Clear();
        }

        /// <summary>
        /// Reorder segments after modifications
        /// </summary>
        private void ReorderSegments()
        {
            for (int i = 0; i < Segments.Count; i++)
            {
                Segments[i].Order = i + 1;
            }
        }

        /// <summary>
        /// Get segment audio paths as tuples for waveform generation
        /// </summary>
        public List<(string name, string filePath)> GetSegmentAudioPaths()
        {
            return Segments
                .Where(s => !string.IsNullOrEmpty(s.AudioPath))
                .Select(s => (s.DisplayName, s.AudioPath))
                .ToList();
        }

        /// <summary>
        /// Create a single-commercial capsule
        /// </summary>
        public static Capsule FromSingleCommercial(Commercial commercial, string audioPath)
        {
            var capsule = new Capsule
            {
                Name = commercial.SanitizedSpotName
            };
            capsule.AddCommercial(commercial, audioPath);
            return capsule;
        }

        /// <summary>
        /// Get sanitized capsule name for filename
        /// </summary>
        public string SanitizedName => Commercial.SanitizeForFilename(Name);

        public override string ToString() =>
            $"{Name} ({SegmentCount} segments, {TotalDurationFormatted})";
    }
}
