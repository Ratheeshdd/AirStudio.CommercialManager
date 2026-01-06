using System;

namespace AirStudio.CommercialManager.Core.Models
{
    /// <summary>
    /// Represents an item in the trash (soft-deleted commercial)
    /// </summary>
    public class TrashItem
    {
        /// <summary>
        /// Original commercial ID from database
        /// </summary>
        public int CommercialId { get; set; }

        /// <summary>
        /// Spot name of the commercial
        /// </summary>
        public string SpotName { get; set; }

        /// <summary>
        /// Agency name
        /// </summary>
        public string Agency { get; set; }

        /// <summary>
        /// Original filename
        /// </summary>
        public string Filename { get; set; }

        /// <summary>
        /// Original path before deletion
        /// </summary>
        public string OriginalPath { get; set; }

        /// <summary>
        /// Path to the file in trash folder
        /// </summary>
        public string TrashPath { get; set; }

        /// <summary>
        /// Date when the item was deleted
        /// </summary>
        public DateTime DeletedDate { get; set; }

        /// <summary>
        /// Date when the item will be permanently deleted
        /// </summary>
        public DateTime ExpiresDate { get; set; }

        /// <summary>
        /// Size of the file in bytes
        /// </summary>
        public long FileSizeBytes { get; set; }

        /// <summary>
        /// Date when the commercial was originally created
        /// </summary>
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// Duration of the commercial
        /// </summary>
        public string Duration { get; set; }

        /// <summary>
        /// Days remaining until permanent deletion
        /// </summary>
        public int DaysUntilExpiration => Math.Max(0, (ExpiresDate.Date - DateTime.Today).Days);

        /// <summary>
        /// Display-friendly expiration info
        /// </summary>
        public string ExpirationDisplay
        {
            get
            {
                var days = DaysUntilExpiration;
                if (days == 0) return "Expires today";
                if (days == 1) return "1 day remaining";
                return $"{days} days remaining";
            }
        }

        /// <summary>
        /// File size in human-readable format
        /// </summary>
        public string FileSizeDisplay
        {
            get
            {
                if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
                if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024.0:F1} KB";
                return $"{FileSizeBytes / (1024.0 * 1024.0):F2} MB";
            }
        }

        /// <summary>
        /// Check if this item has expired and should be permanently deleted
        /// </summary>
        public bool IsExpired => DateTime.Now >= ExpiresDate;

        /// <summary>
        /// For UI selection binding
        /// </summary>
        public bool IsSelected { get; set; }
    }
}
