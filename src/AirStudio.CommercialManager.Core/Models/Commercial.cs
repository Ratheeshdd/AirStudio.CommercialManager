using System;

namespace AirStudio.CommercialManager.Core.Models
{
    /// <summary>
    /// Represents a commercial from the commercials table
    /// </summary>
    public class Commercial
    {
        /// <summary>
        /// Primary key (auto-increment)
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Agency code (foreign key to agency table)
        /// </summary>
        public int Code { get; set; }

        /// <summary>
        /// Agency name (denormalized for display)
        /// </summary>
        public string Agency { get; set; }

        /// <summary>
        /// Spot name/code (used for filename)
        /// </summary>
        public string Spot { get; set; }

        /// <summary>
        /// Commercial title
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Duration in HH:mm:ss format
        /// </summary>
        public string Duration { get; set; }

        /// <summary>
        /// Other information/notes
        /// </summary>
        public string Otherinfo { get; set; }

        /// <summary>
        /// Audio filename (e.g., GONG2.WAV)
        /// </summary>
        public string Filename { get; set; }

        /// <summary>
        /// User who created/modified the record
        /// </summary>
        public string User { get; set; }

        /// <summary>
        /// Last update timestamp
        /// </summary>
        public DateTime LastUpdate { get; set; }

        /// <summary>
        /// Date when the commercial was added
        /// </summary>
        public DateTime DateIn { get; set; }

        /// <summary>
        /// Status of the commercial (Active, Deleted, Purged)
        /// </summary>
        public string Status { get; set; } = "Active";

        /// <summary>
        /// Date when the commercial was deleted (soft delete)
        /// </summary>
        public DateTime? DeletedDate { get; set; }

        /// <summary>
        /// Duration as TimeSpan (parsed from Duration string)
        /// </summary>
        public TimeSpan DurationTimeSpan
        {
            get
            {
                if (TimeSpan.TryParse(Duration, out var ts))
                    return ts;
                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Duration in total seconds
        /// </summary>
        public double DurationSeconds => DurationTimeSpan.TotalSeconds;

        /// <summary>
        /// Validate the commercial data
        /// </summary>
        public bool IsValid =>
            Code > 0 &&
            !string.IsNullOrWhiteSpace(Spot) &&
            !string.IsNullOrWhiteSpace(Filename);

        /// <summary>
        /// Get the sanitized spot name for filename
        /// </summary>
        public string SanitizedSpotName => SanitizeForFilename(Spot);

        /// <summary>
        /// Get the expected filename (SPOTNAME.WAV uppercase)
        /// </summary>
        public string ExpectedFilename => $"{SanitizedSpotName}.WAV";

        /// <summary>
        /// Check if this commercial is active (not deleted)
        /// </summary>
        public bool IsActive => string.IsNullOrEmpty(Status) || Status == "Active";

        /// <summary>
        /// Year the commercial was added
        /// </summary>
        public int YearAdded => DateIn.Year > 1900 ? DateIn.Year : LastUpdate.Year;

        /// <summary>
        /// Create a copy of this commercial
        /// </summary>
        public Commercial Clone()
        {
            return new Commercial
            {
                Id = Id,
                Code = Code,
                Agency = Agency,
                Spot = Spot,
                Title = Title,
                Duration = Duration,
                Otherinfo = Otherinfo,
                Filename = Filename,
                User = User,
                LastUpdate = LastUpdate,
                DateIn = DateIn,
                Status = Status,
                DeletedDate = DeletedDate
            };
        }

        /// <summary>
        /// Sanitize a string for use as a filename
        /// - Keep spaces
        /// - Remove invalid filename characters
        /// - Convert to uppercase
        /// </summary>
        public static string SanitizeForFilename(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var invalidChars = new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*', '\t', '\n', '\r' };
            var result = input;

            foreach (var c in invalidChars)
            {
                result = result.Replace(c.ToString(), string.Empty);
            }

            return result.Trim().ToUpperInvariant();
        }

        public override string ToString() => $"{Spot} - {Title}";
    }
}
