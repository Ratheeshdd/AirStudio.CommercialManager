using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AirStudio.CommercialManager.Core.Models
{
    /// <summary>
    /// Represents a single entry (row) in a TAG file
    /// </summary>
    public class TagEntry
    {
        /// <summary>
        /// Category: 2 for commercial cut, 1 for terminator
        /// </summary>
        public int Category { get; set; }

        /// <summary>
        /// Transmission time (HH:mm:ss)
        /// </summary>
        public TimeSpan TxTime { get; set; }

        /// <summary>
        /// Spot/cut name
        /// </summary>
        public string SpotName { get; set; }

        /// <summary>
        /// Capsule name
        /// </summary>
        public string CapsuleName { get; set; }

        /// <summary>
        /// Duration (HH:mm:ss)
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Flag (always 0)
        /// </summary>
        public int Flag { get; set; } = 0;

        /// <summary>
        /// Duration in seconds (precise float)
        /// </summary>
        public double DurationSeconds { get; set; }

        /// <summary>
        /// Full audio path
        /// </summary>
        public string AudioPath { get; set; }

        /// <summary>
        /// Agency code from library metadata
        /// </summary>
        public int AgencyCode { get; set; }

        /// <summary>
        /// Sequence token (NN:01:XX format)
        /// </summary>
        public string SequenceToken { get; set; }

        /// <summary>
        /// Is this entry a terminator line
        /// </summary>
        public bool IsTerminator => Category == 1;

        /// <summary>
        /// Format duration as HH:mm:ss string
        /// </summary>
        public string DurationFormatted => Duration.ToString(@"hh\:mm\:ss");

        /// <summary>
        /// Format TxTime as HH:mm:ss string
        /// </summary>
        public string TxTimeFormatted => TxTime.ToString(@"hh\:mm\:ss");

        /// <summary>
        /// Convert entry to TAB-separated line
        /// </summary>
        public string ToTagLine()
        {
            var fields = new[]
            {
                Category.ToString(),
                TxTimeFormatted,
                SpotName ?? "",
                CapsuleName ?? "",
                DurationFormatted,
                Flag.ToString(),
                DurationSeconds.ToString("F2", CultureInfo.InvariantCulture),
                AudioPath ?? "",
                AgencyCode.ToString(),
                SequenceToken ?? ""
            };

            return string.Join("\t", fields);
        }

        /// <summary>
        /// Parse a TAG line into an entry
        /// </summary>
        public static TagEntry Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            var fields = line.Split('\t');
            if (fields.Length < 10)
                return null;

            var entry = new TagEntry();

            // Category
            if (int.TryParse(fields[0], out var category))
                entry.Category = category;

            // TxTime
            if (TimeSpan.TryParse(fields[1], out var txTime))
                entry.TxTime = txTime;

            entry.SpotName = fields[2];
            entry.CapsuleName = fields[3];

            // Duration
            if (TimeSpan.TryParse(fields[4], out var duration))
                entry.Duration = duration;

            // Flag
            if (int.TryParse(fields[5], out var flag))
                entry.Flag = flag;

            // Duration seconds
            if (double.TryParse(fields[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var durationSec))
                entry.DurationSeconds = durationSec;

            entry.AudioPath = fields[7];

            // Agency code
            if (int.TryParse(fields[8], out var agencyCode))
                entry.AgencyCode = agencyCode;

            entry.SequenceToken = fields[9];

            return entry;
        }
    }

    /// <summary>
    /// Represents a complete TAG file
    /// </summary>
    public class TagFile
    {
        /// <summary>
        /// TAG file path
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// TAG file name (without path)
        /// </summary>
        public string FileName => Path.GetFileName(FilePath);

        /// <summary>
        /// Capsule name (extracted from filename or entries)
        /// </summary>
        public string CapsuleName { get; set; }

        /// <summary>
        /// Transmission time
        /// </summary>
        public TimeSpan TxTime { get; set; }

        /// <summary>
        /// Transmission date (FromDate)
        /// </summary>
        public DateTime TxDate { get; set; }

        /// <summary>
        /// End date (ToDate/Validity)
        /// </summary>
        public DateTime ToDate { get; set; }

        /// <summary>
        /// Repeat days (R = ToDate - TxDate)
        /// </summary>
        public int RepeatDays => (int)(ToDate - TxDate).TotalDays;

        /// <summary>
        /// Sequence base NN value
        /// </summary>
        public int SequenceBaseNN { get; set; } = 27;

        /// <summary>
        /// List of entries (commercial cuts)
        /// </summary>
        public List<TagEntry> Entries { get; set; } = new List<TagEntry>();

        /// <summary>
        /// Get commercial entries (excluding terminator)
        /// </summary>
        public List<TagEntry> CommercialEntries =>
            Entries.Where(e => !e.IsTerminator).ToList();

        /// <summary>
        /// Get the terminator entry
        /// </summary>
        public TagEntry TerminatorEntry =>
            Entries.FirstOrDefault(e => e.IsTerminator);

        /// <summary>
        /// Number of commercial cuts
        /// </summary>
        public int CutCount => CommercialEntries.Count;

        /// <summary>
        /// Is this a single-commercial TAG
        /// </summary>
        public bool IsSingleCommercial => CutCount == 1;

        /// <summary>
        /// Total duration of all cuts
        /// </summary>
        public TimeSpan TotalDuration =>
            TimeSpan.FromSeconds(CommercialEntries.Sum(e => e.DurationSeconds));

        /// <summary>
        /// Total duration formatted
        /// </summary>
        public string TotalDurationFormatted => TotalDuration.ToString(@"hh\:mm\:ss");

        /// <summary>
        /// First cut spot name (for Title field)
        /// </summary>
        public string FirstCutSpotName =>
            CommercialEntries.FirstOrDefault()?.SpotName ?? "";

        /// <summary>
        /// Generate TAG filename
        /// Format: HHMMSS_DDMMYY(R)_CAPSULE NAME.TAG
        /// </summary>
        public string GenerateFileName()
        {
            var timeStr = TxTime.ToString(@"hhmmss");
            var dateStr = TxDate.ToString("ddMMyy");
            var repeatStr = $"({RepeatDays})";
            var capsuleSafe = SanitizeForFilename(CapsuleName);

            return $"{timeStr}_{dateStr}{repeatStr}_{capsuleSafe}.TAG";
        }

        /// <summary>
        /// Generate full TAG file content
        /// </summary>
        public string GenerateContent()
        {
            var sb = new StringBuilder();

            foreach (var entry in Entries)
            {
                sb.AppendLine(entry.ToTagLine());
            }

            return sb.ToString();
        }

        /// <summary>
        /// Create TagFile from a Capsule and schedule info
        /// </summary>
        public static TagFile FromCapsule(
            Capsule capsule,
            TimeSpan txTime,
            DateTime fromDate,
            DateTime toDate,
            string commercialsBasePath,
            int sequenceBaseNN = 27)
        {
            var tagFile = new TagFile
            {
                CapsuleName = capsule.SanitizedName,
                TxTime = txTime,
                TxDate = fromDate,
                ToDate = toDate,
                SequenceBaseNN = sequenceBaseNN
            };

            // Create entries for each segment
            int sequence = 1;
            foreach (var segment in capsule.Segments)
            {
                var audioPath = Path.Combine(commercialsBasePath, segment.Commercial.Filename);

                var entry = new TagEntry
                {
                    Category = 2,
                    TxTime = txTime,
                    SpotName = segment.Commercial.Spot,
                    CapsuleName = capsule.SanitizedName,
                    Duration = segment.Duration,
                    Flag = 0,
                    DurationSeconds = segment.Duration.TotalSeconds,
                    AudioPath = audioPath,
                    AgencyCode = segment.Commercial.Code,
                    SequenceToken = $"{sequenceBaseNN:D2}:01:{sequence:D2}"
                };

                tagFile.Entries.Add(entry);
                sequence++;
            }

            // Add terminator line
            var firstEntry = tagFile.Entries.FirstOrDefault();
            var terminator = new TagEntry
            {
                Category = 1,
                TxTime = txTime,
                SpotName = firstEntry?.SpotName ?? "",
                CapsuleName = capsule.SanitizedName,
                Duration = firstEntry?.Duration ?? TimeSpan.Zero,
                Flag = 0,
                DurationSeconds = firstEntry?.DurationSeconds ?? 0,
                AudioPath = firstEntry?.AudioPath ?? "",
                AgencyCode = firstEntry?.AgencyCode ?? 0,
                SequenceToken = $"{sequenceBaseNN:D2}:01:99"
            };

            tagFile.Entries.Add(terminator);

            return tagFile;
        }

        /// <summary>
        /// Parse a TAG file from content
        /// </summary>
        public static TagFile Parse(string content, string filePath = null)
        {
            var tagFile = new TagFile { FilePath = filePath };

            if (string.IsNullOrWhiteSpace(content))
                return tagFile;

            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var entry = TagEntry.Parse(line);
                if (entry != null)
                {
                    tagFile.Entries.Add(entry);
                }
            }

            // Extract metadata from entries and filename
            if (tagFile.Entries.Count > 0)
            {
                var firstEntry = tagFile.Entries.First();
                tagFile.CapsuleName = firstEntry.CapsuleName;
                tagFile.TxTime = firstEntry.TxTime;

                // Try to extract NN from sequence token
                if (!string.IsNullOrEmpty(firstEntry.SequenceToken))
                {
                    var parts = firstEntry.SequenceToken.Split(':');
                    if (parts.Length >= 1 && int.TryParse(parts[0], out var nn))
                    {
                        tagFile.SequenceBaseNN = nn;
                    }
                }
            }

            // Try to parse date from filename
            if (!string.IsNullOrEmpty(filePath))
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                tagFile.ParseDatesFromFileName(fileName);
            }

            return tagFile;
        }

        /// <summary>
        /// Parse dates from TAG filename
        /// Format: HHMMSS_DDMMYY(R)_CAPSULE NAME
        /// </summary>
        private void ParseDatesFromFileName(string fileName)
        {
            try
            {
                // Expected format: 064350_010126(2)_BEFORE MORNG REG NEWS
                var parts = fileName.Split('_');
                if (parts.Length < 2)
                    return;

                // Parse date part: DDMMYY(R)
                var datePart = parts[1];
                var parenIndex = datePart.IndexOf('(');

                string dateStr;
                int repeatDays = 0;

                if (parenIndex > 0)
                {
                    dateStr = datePart.Substring(0, parenIndex);
                    var repeatStr = datePart.Substring(parenIndex + 1).TrimEnd(')');
                    int.TryParse(repeatStr, out repeatDays);
                }
                else
                {
                    dateStr = datePart;
                }

                // Parse DDMMYY
                if (dateStr.Length == 6)
                {
                    var day = int.Parse(dateStr.Substring(0, 2));
                    var month = int.Parse(dateStr.Substring(2, 2));
                    var year = 2000 + int.Parse(dateStr.Substring(4, 2));

                    TxDate = new DateTime(year, month, day);
                    ToDate = TxDate.AddDays(repeatDays);
                }
            }
            catch
            {
                // Ignore parsing errors, use defaults
            }
        }

        /// <summary>
        /// Sanitize string for use in filename
        /// </summary>
        private static string SanitizeForFilename(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "CAPSULE";

            var invalidChars = new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*', '\t', '\n', '\r' };
            var result = input;

            foreach (var c in invalidChars)
            {
                result = result.Replace(c.ToString(), string.Empty);
            }

            return result.Trim().ToUpperInvariant();
        }

        public override string ToString() =>
            $"{FileName ?? GenerateFileName()} ({CutCount} cuts, {TotalDurationFormatted})";
    }
}
