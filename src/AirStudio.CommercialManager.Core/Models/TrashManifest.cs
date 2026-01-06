using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AirStudio.CommercialManager.Core.Models
{
    /// <summary>
    /// Manifest file structure for tracking items in trash
    /// Stored as manifest.json in each .trash folder
    /// </summary>
    public class TrashManifest
    {
        /// <summary>
        /// Version of the manifest format
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// Channel name this trash belongs to
        /// </summary>
        [JsonPropertyName("channel")]
        public string Channel { get; set; }

        /// <summary>
        /// Last time the manifest was updated
        /// </summary>
        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// Items currently in trash
        /// </summary>
        [JsonPropertyName("items")]
        public List<TrashManifestItem> Items { get; set; } = new List<TrashManifestItem>();
    }

    /// <summary>
    /// Individual item entry in the trash manifest
    /// </summary>
    public class TrashManifestItem
    {
        /// <summary>
        /// Original commercial ID from database
        /// </summary>
        [JsonPropertyName("commercialId")]
        public int CommercialId { get; set; }

        /// <summary>
        /// Spot name
        /// </summary>
        [JsonPropertyName("spotName")]
        public string SpotName { get; set; }

        /// <summary>
        /// Agency name
        /// </summary>
        [JsonPropertyName("agency")]
        public string Agency { get; set; }

        /// <summary>
        /// Filename (not full path)
        /// </summary>
        [JsonPropertyName("filename")]
        public string Filename { get; set; }

        /// <summary>
        /// Original path before deletion
        /// </summary>
        [JsonPropertyName("originalPath")]
        public string OriginalPath { get; set; }

        /// <summary>
        /// Date when the item was moved to trash
        /// </summary>
        [JsonPropertyName("deletedDate")]
        public DateTime DeletedDate { get; set; }

        /// <summary>
        /// Date when the item will be permanently purged
        /// </summary>
        [JsonPropertyName("expiresDate")]
        public DateTime ExpiresDate { get; set; }

        /// <summary>
        /// File size in bytes
        /// </summary>
        [JsonPropertyName("fileSizeBytes")]
        public long FileSizeBytes { get; set; }

        /// <summary>
        /// Original creation date
        /// </summary>
        [JsonPropertyName("createdDate")]
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// Duration string
        /// </summary>
        [JsonPropertyName("duration")]
        public string Duration { get; set; }

        /// <summary>
        /// Convert to TrashItem for UI display
        /// </summary>
        public TrashItem ToTrashItem(string trashFolderPath)
        {
            return new TrashItem
            {
                CommercialId = CommercialId,
                SpotName = SpotName,
                Agency = Agency,
                Filename = Filename,
                OriginalPath = OriginalPath,
                TrashPath = System.IO.Path.Combine(trashFolderPath, Filename),
                DeletedDate = DeletedDate,
                ExpiresDate = ExpiresDate,
                FileSizeBytes = FileSizeBytes,
                CreatedDate = CreatedDate,
                Duration = Duration
            };
        }

        /// <summary>
        /// Create from Commercial and file info
        /// </summary>
        public static TrashManifestItem FromCommercial(Commercial commercial, string originalPath, long fileSize, int retentionDays = 30)
        {
            return new TrashManifestItem
            {
                CommercialId = commercial.Id,
                SpotName = commercial.Spot,
                Agency = commercial.Agency,
                Filename = commercial.Filename,
                OriginalPath = originalPath,
                DeletedDate = DateTime.Now,
                ExpiresDate = DateTime.Now.AddDays(retentionDays),
                FileSizeBytes = fileSize,
                CreatedDate = commercial.DateIn,
                Duration = commercial.Duration
            };
        }
    }
}
