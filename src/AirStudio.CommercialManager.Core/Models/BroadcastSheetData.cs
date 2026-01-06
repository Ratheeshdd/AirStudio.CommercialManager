using System;
using System.Collections.Generic;

namespace AirStudio.CommercialManager.Core.Models
{
    /// <summary>
    /// Data model for broadcast sheet PDF generation
    /// </summary>
    public class BroadcastSheetData
    {
        /// <summary>
        /// Channel name
        /// </summary>
        public string ChannelName { get; set; }

        /// <summary>
        /// Start date of the report period
        /// </summary>
        public DateTime FromDate { get; set; }

        /// <summary>
        /// End date of the report period
        /// </summary>
        public DateTime ToDate { get; set; }

        /// <summary>
        /// Date/time when the report was generated
        /// </summary>
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// User who generated the report
        /// </summary>
        public string GeneratedBy { get; set; }

        /// <summary>
        /// Scheduled items grouped by date
        /// </summary>
        public List<BroadcastSheetDay> Days { get; set; } = new List<BroadcastSheetDay>();

        /// <summary>
        /// Report options
        /// </summary>
        public BroadcastSheetOptions Options { get; set; } = new BroadcastSheetOptions();

        /// <summary>
        /// Summary statistics
        /// </summary>
        public BroadcastSheetSummary Summary { get; set; } = new BroadcastSheetSummary();

        /// <summary>
        /// Period display string
        /// </summary>
        public string PeriodDisplay
        {
            get
            {
                if (FromDate.Date == ToDate.Date)
                    return FromDate.ToString("dd MMMM yyyy");
                return $"{FromDate:dd MMMM yyyy} to {ToDate:dd MMMM yyyy}";
            }
        }
    }

    /// <summary>
    /// A single day's schedule for the broadcast sheet
    /// </summary>
    public class BroadcastSheetDay
    {
        /// <summary>
        /// Date of this day
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// Day display (e.g., "06-JANUARY-2026 (MONDAY)")
        /// </summary>
        public string DayDisplay => $"{Date:dd-MMMM-yyyy} ({Date:dddd})".ToUpper();

        /// <summary>
        /// Scheduled capsules for this day
        /// </summary>
        public List<BroadcastSheetCapsule> Capsules { get; set; } = new List<BroadcastSheetCapsule>();
    }

    /// <summary>
    /// A single capsule/schedule entry
    /// </summary>
    public class BroadcastSheetCapsule
    {
        /// <summary>
        /// Transmission time
        /// </summary>
        public string TxTime { get; set; }

        /// <summary>
        /// Capsule name
        /// </summary>
        public string CapsuleName { get; set; }

        /// <summary>
        /// Total duration
        /// </summary>
        public string Duration { get; set; }

        /// <summary>
        /// Validity/To date
        /// </summary>
        public DateTime ValidUntil { get; set; }

        /// <summary>
        /// User who scheduled
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// Mobile number
        /// </summary>
        public string MobileNo { get; set; }

        /// <summary>
        /// Individual spots/cuts in this capsule
        /// </summary>
        public List<BroadcastSheetSpot> Spots { get; set; } = new List<BroadcastSheetSpot>();

        /// <summary>
        /// Spots count display
        /// </summary>
        public string SpotsDisplay => Spots.Count > 0 ? $"{Spots.Count} spot{(Spots.Count != 1 ? "s" : "")}" : "--";
    }

    /// <summary>
    /// Individual spot/commercial in a capsule
    /// </summary>
    public class BroadcastSheetSpot
    {
        /// <summary>
        /// Spot name
        /// </summary>
        public string SpotName { get; set; }

        /// <summary>
        /// Duration
        /// </summary>
        public string Duration { get; set; }

        /// <summary>
        /// Agency name
        /// </summary>
        public string Agency { get; set; }

        /// <summary>
        /// Agency code
        /// </summary>
        public int AgencyCode { get; set; }
    }

    /// <summary>
    /// Options for PDF generation
    /// </summary>
    public class BroadcastSheetOptions
    {
        /// <summary>
        /// Include individual spot details
        /// </summary>
        public bool IncludeSpotDetails { get; set; } = true;

        /// <summary>
        /// Include agency information
        /// </summary>
        public bool IncludeAgencyInfo { get; set; } = true;

        /// <summary>
        /// Include user info (who scheduled)
        /// </summary>
        public bool IncludeUserInfo { get; set; } = true;
    }

    /// <summary>
    /// Summary statistics for the broadcast sheet
    /// </summary>
    public class BroadcastSheetSummary
    {
        /// <summary>
        /// Total number of schedules
        /// </summary>
        public int TotalSchedules { get; set; }

        /// <summary>
        /// Total duration of all commercials
        /// </summary>
        public TimeSpan TotalDuration { get; set; }

        /// <summary>
        /// Number of unique commercials/spots
        /// </summary>
        public int UniqueCommercials { get; set; }

        /// <summary>
        /// Number of unique agencies
        /// </summary>
        public int UniqueAgencies { get; set; }

        /// <summary>
        /// Total duration formatted
        /// </summary>
        public string TotalDurationDisplay => TotalDuration.ToString(@"hh\:mm\:ss");
    }
}
