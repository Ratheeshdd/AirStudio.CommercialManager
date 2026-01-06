using System;

namespace AirStudio.CommercialManager.Core.Models
{
    /// <summary>
    /// Represents scheduling information for a capsule
    /// </summary>
    public class Schedule
    {
        /// <summary>
        /// The capsule to schedule
        /// </summary>
        public Capsule Capsule { get; set; }

        /// <summary>
        /// Transmission time (HH:mm:ss)
        /// </summary>
        public TimeSpan TxTime { get; set; }

        /// <summary>
        /// Start date (FromDate / TxDate)
        /// </summary>
        public DateTime FromDate { get; set; }

        /// <summary>
        /// End date (ToDate / Validity)
        /// </summary>
        public DateTime ToDate { get; set; }

        /// <summary>
        /// User name for the schedule
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// Mobile number for the schedule
        /// </summary>
        public string MobileNo { get; set; }

        /// <summary>
        /// Sequence base NN value (default 27)
        /// </summary>
        public int SequenceBaseNN { get; set; } = 27;

        /// <summary>
        /// Repeat days (ToDate - FromDate)
        /// </summary>
        public int RepeatDays => Math.Max(0, (int)(ToDate - FromDate).TotalDays);

        /// <summary>
        /// Is this a repeating schedule
        /// </summary>
        public bool IsRepeating => RepeatDays > 0;

        /// <summary>
        /// Validate the schedule
        /// </summary>
        public bool IsValid =>
            Capsule != null &&
            Capsule.IsValid &&
            FromDate >= DateTime.Today &&
            ToDate >= FromDate;

        /// <summary>
        /// Get validation error message
        /// </summary>
        public string GetValidationError()
        {
            if (Capsule == null || !Capsule.HasSegments)
                return "Please add at least one commercial to the capsule.";

            if (string.IsNullOrWhiteSpace(Capsule.Name))
                return "Please enter a capsule name.";

            if (FromDate < DateTime.Today)
                return "From date cannot be in the past.";

            if (ToDate < FromDate)
                return "To date cannot be before from date.";

            return null;
        }

        /// <summary>
        /// Create a TAG file from this schedule
        /// </summary>
        public TagFile CreateTagFile(string commercialsBasePath)
        {
            return TagFile.FromCapsule(
                Capsule,
                TxTime,
                FromDate,
                ToDate,
                commercialsBasePath,
                SequenceBaseNN);
        }

        /// <summary>
        /// Format the schedule as a display string
        /// </summary>
        public string ToDisplayString()
        {
            var txTimeStr = TxTime.ToString(@"hh\:mm\:ss");
            var fromStr = FromDate.ToString("dd/MM/yyyy");
            var toStr = ToDate.ToString("dd/MM/yyyy");

            if (IsRepeating)
            {
                return $"{Capsule?.Name} at {txTimeStr}, {fromStr} to {toStr} ({RepeatDays + 1} days)";
            }
            else
            {
                return $"{Capsule?.Name} at {txTimeStr} on {fromStr}";
            }
        }

        public override string ToString() => ToDisplayString();
    }
}
