using System;
using System.Collections.Generic;

namespace AirStudio.CommercialManager.Core.Models
{
    /// <summary>
    /// Represents a scheduled commercial entry from the playlist table
    /// Used to display schedules in the ScheduledCommercials grid
    /// </summary>
    public class ScheduledCommercial
    {
        /// <summary>
        /// Primary key from playlist table
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Transmission date (TxDate)
        /// </summary>
        public DateTime TxDate { get; set; }

        /// <summary>
        /// Transmission time as string (HH:mm:ss)
        /// </summary>
        public string TxTime { get; set; }

        /// <summary>
        /// Capsule/Programme name
        /// </summary>
        public string CapsuleName { get; set; }

        /// <summary>
        /// Title (first cut's spot name)
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Duration in HH:mm:ss format
        /// </summary>
        public string Duration { get; set; }

        /// <summary>
        /// Validity/To Date
        /// </summary>
        public DateTime ToDate { get; set; }

        /// <summary>
        /// Full path to the TAG file (MainPath column)
        /// </summary>
        public string TagFilePath { get; set; }

        /// <summary>
        /// User who scheduled this commercial
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// Mobile number of the user who scheduled
        /// </summary>
        public string MobileNo { get; set; }

        /// <summary>
        /// Windows user who created/modified the record
        /// </summary>
        public string LoginUser { get; set; }

        /// <summary>
        /// Last update timestamp
        /// </summary>
        public DateTime LastUpdate { get; set; }

        /// <summary>
        /// Status indicator (Active, Expired, Pending)
        /// </summary>
        public string Status
        {
            get
            {
                var today = DateTime.Today;
                if (TxDate > today)
                    return "Pending";
                if (ToDate < today)
                    return "Expired";
                return "Active";
            }
        }

        /// <summary>
        /// Display-friendly transmission time (HH:mm format)
        /// </summary>
        public string TxTimeDisplay => TxTime?.Length >= 5 ? TxTime.Substring(0, 5) : TxTime;

        /// <summary>
        /// Number of repeat days (ToDate - TxDate)
        /// </summary>
        public int RepeatDays => (ToDate - TxDate).Days;

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
        /// Get the TAG filename from the full path
        /// </summary>
        public string TagFileName
        {
            get
            {
                if (string.IsNullOrEmpty(TagFilePath))
                    return string.Empty;
                return System.IO.Path.GetFileName(TagFilePath);
            }
        }

        public override string ToString() => $"{TxDate:yyyy-MM-dd} {TxTime} - {CapsuleName}";
    }

    /// <summary>
    /// Item for the Playlist Creator panel
    /// </summary>
    public class PlaylistItem
    {
        /// <summary>
        /// Order/sequence in the playlist (1-based)
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// The commercial/cut
        /// </summary>
        public Commercial Commercial { get; set; }

        /// <summary>
        /// Duration of this item
        /// </summary>
        public TimeSpan Duration => Commercial?.DurationTimeSpan ?? TimeSpan.Zero;

        /// <summary>
        /// Display name for the playlist
        /// </summary>
        public string DisplayName => Commercial?.Spot ?? "Unknown";

        /// <summary>
        /// Agency name
        /// </summary>
        public string Agency => Commercial?.Agency ?? "--";

        /// <summary>
        /// Duration formatted as string
        /// </summary>
        public string DurationDisplay => Duration.ToString(@"hh\:mm\:ss");

        /// <summary>
        /// Is this item selected in the playlist
        /// </summary>
        public bool IsSelected { get; set; }

        public PlaylistItem() { }

        public PlaylistItem(Commercial commercial, int order)
        {
            Commercial = commercial;
            Order = order;
        }
    }
}
