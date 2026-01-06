using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Database;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Core.Services.Library
{
    /// <summary>
    /// Service for managing the trash system for deleted commercials
    /// </summary>
    public class TrashService
    {
        private const string TRASH_FOLDER_NAME = ".trash";
        private const string MANIFEST_FILENAME = "manifest.json";
        private const int DEFAULT_RETENTION_DAYS = 30;

        private readonly Channel _channel;
        private readonly string _channelDatabaseName;

        public TrashService(Channel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _channelDatabaseName = channel.DatabaseName;
        }

        /// <summary>
        /// Move commercials to trash (soft delete)
        /// </summary>
        public async Task<TrashOperationResult> MoveToTrashAsync(List<Commercial> commercials, CancellationToken cancellationToken = default)
        {
            if (commercials == null || commercials.Count == 0)
                return TrashOperationResult.Failed("No commercials provided");

            var result = new TrashOperationResult();
            var targets = _channel.GetAccessibleTargets();

            if (targets.Count == 0)
            {
                return TrashOperationResult.Failed("No accessible X targets available");
            }

            foreach (var commercial in commercials)
            {
                try
                {
                    var filesMoved = 0;
                    var filesTotal = 0;

                    // Move files on each target
                    foreach (var targetPath in targets)
                    {
                        filesTotal++;
                        var commercialsPath = Path.Combine(targetPath, "Commercials");
                        var sourcePath = Path.Combine(commercialsPath, commercial.Filename);
                        var trashPath = Path.Combine(commercialsPath, TRASH_FOLDER_NAME);
                        var destPath = Path.Combine(trashPath, commercial.Filename);

                        if (File.Exists(sourcePath))
                        {
                            // Ensure trash folder exists
                            if (!Directory.Exists(trashPath))
                            {
                                Directory.CreateDirectory(trashPath);
                            }

                            // Move file to trash
                            File.Move(sourcePath, destPath, overwrite: true);
                            filesMoved++;

                            // Update manifest
                            var fileInfo = new FileInfo(destPath);
                            await UpdateManifestAsync(trashPath, commercial, sourcePath, fileInfo.Length, addItem: true, cancellationToken);
                        }
                    }

                    if (filesMoved > 0)
                    {
                        // Update database - mark as deleted
                        await MarkAsDeletedInDatabaseAsync(commercial.Id, cancellationToken);
                        result.SuccessCount++;
                        LogService.Info($"Moved to trash: {commercial.Spot} ({filesMoved}/{filesTotal} files)");
                    }
                    else
                    {
                        result.Errors.Add($"{commercial.Spot}: No files found to move");
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error($"Failed to move {commercial.Spot} to trash", ex);
                    result.Errors.Add($"{commercial.Spot}: {ex.Message}");
                }
            }

            result.Success = result.SuccessCount > 0;
            return result;
        }

        /// <summary>
        /// Restore commercials from trash
        /// </summary>
        public async Task<TrashOperationResult> RestoreFromTrashAsync(List<TrashItem> items, CancellationToken cancellationToken = default)
        {
            if (items == null || items.Count == 0)
                return TrashOperationResult.Failed("No items provided");

            var result = new TrashOperationResult();
            var targets = _channel.GetAccessibleTargets();

            foreach (var item in items)
            {
                try
                {
                    var filesRestored = 0;

                    foreach (var targetPath in targets)
                    {
                        var commercialsPath = Path.Combine(targetPath, "Commercials");
                        var trashPath = Path.Combine(commercialsPath, TRASH_FOLDER_NAME);
                        var sourcePath = Path.Combine(trashPath, item.Filename);
                        var destPath = Path.Combine(commercialsPath, item.Filename);

                        if (File.Exists(sourcePath))
                        {
                            File.Move(sourcePath, destPath, overwrite: true);
                            filesRestored++;

                            // Update manifest
                            await UpdateManifestAsync(trashPath, item, removeItem: true, cancellationToken);
                        }
                    }

                    if (filesRestored > 0)
                    {
                        // Update database - restore from deleted state
                        await RestoreInDatabaseAsync(item.CommercialId, cancellationToken);
                        result.SuccessCount++;
                        LogService.Info($"Restored from trash: {item.SpotName}");
                    }
                    else
                    {
                        result.Errors.Add($"{item.SpotName}: No files found in trash");
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error($"Failed to restore {item.SpotName} from trash", ex);
                    result.Errors.Add($"{item.SpotName}: {ex.Message}");
                }
            }

            result.Success = result.SuccessCount > 0;
            return result;
        }

        /// <summary>
        /// Permanently delete items from trash
        /// </summary>
        public async Task<TrashOperationResult> PermanentDeleteAsync(List<TrashItem> items, CancellationToken cancellationToken = default)
        {
            if (items == null || items.Count == 0)
                return TrashOperationResult.Failed("No items provided");

            var result = new TrashOperationResult();
            var targets = _channel.GetAccessibleTargets();

            foreach (var item in items)
            {
                try
                {
                    var filesDeleted = 0;

                    foreach (var targetPath in targets)
                    {
                        var commercialsPath = Path.Combine(targetPath, "Commercials");
                        var trashPath = Path.Combine(commercialsPath, TRASH_FOLDER_NAME);
                        var filePath = Path.Combine(trashPath, item.Filename);

                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                            filesDeleted++;

                            // Update manifest
                            await UpdateManifestAsync(trashPath, item, removeItem: true, cancellationToken);
                        }
                    }

                    if (filesDeleted > 0)
                    {
                        // Update database - mark as purged
                        await MarkAsPurgedInDatabaseAsync(item.CommercialId, cancellationToken);
                        result.SuccessCount++;
                        LogService.Info($"Permanently deleted: {item.SpotName}");
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error($"Failed to permanently delete {item.SpotName}", ex);
                    result.Errors.Add($"{item.SpotName}: {ex.Message}");
                }
            }

            result.Success = result.SuccessCount > 0;
            return result;
        }

        /// <summary>
        /// Auto-purge expired items from trash
        /// </summary>
        public async Task<TrashOperationResult> AutoPurgeExpiredAsync(CancellationToken cancellationToken = default)
        {
            var result = new TrashOperationResult();

            try
            {
                var trashItems = await GetTrashItemsAsync(cancellationToken);
                var expiredItems = trashItems.Where(i => i.IsExpired).ToList();

                if (expiredItems.Count > 0)
                {
                    LogService.Info($"Auto-purging {expiredItems.Count} expired trash items for {_channel.Name}");
                    return await PermanentDeleteAsync(expiredItems, cancellationToken);
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                LogService.Error($"Auto-purge failed for {_channel.Name}", ex);
                result.Errors.Add(ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Get all items currently in trash
        /// </summary>
        public async Task<List<TrashItem>> GetTrashItemsAsync(CancellationToken cancellationToken = default)
        {
            var items = new Dictionary<int, TrashItem>(); // Use commercial ID as key to dedupe
            var targets = _channel.GetAccessibleTargets();

            foreach (var targetPath in targets)
            {
                var commercialsPath = Path.Combine(targetPath, "Commercials");
                var trashPath = Path.Combine(commercialsPath, TRASH_FOLDER_NAME);
                var manifestPath = Path.Combine(trashPath, MANIFEST_FILENAME);

                if (File.Exists(manifestPath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                        var manifest = JsonSerializer.Deserialize<TrashManifest>(json);

                        if (manifest?.Items != null)
                        {
                            foreach (var manifestItem in manifest.Items)
                            {
                                if (!items.ContainsKey(manifestItem.CommercialId))
                                {
                                    items[manifestItem.CommercialId] = manifestItem.ToTrashItem(trashPath);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Warning($"Failed to read trash manifest from {targetPath}: {ex.Message}");
                    }
                }
            }

            return items.Values.OrderByDescending(i => i.DeletedDate).ToList();
        }

        /// <summary>
        /// Get trash statistics
        /// </summary>
        public async Task<TrashStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
        {
            var items = await GetTrashItemsAsync(cancellationToken);
            return new TrashStatistics
            {
                TotalItems = items.Count,
                ExpiringToday = items.Count(i => i.DaysUntilExpiration == 0),
                ExpiringSoon = items.Count(i => i.DaysUntilExpiration <= 7),
                TotalSizeBytes = items.Sum(i => i.FileSizeBytes)
            };
        }

        #region Private Helper Methods

        private async Task UpdateManifestAsync(string trashPath, Commercial commercial, string originalPath, long fileSize, bool addItem, CancellationToken cancellationToken)
        {
            var manifestPath = Path.Combine(trashPath, MANIFEST_FILENAME);
            var manifest = await LoadOrCreateManifestAsync(manifestPath, cancellationToken);

            if (addItem)
            {
                // Remove existing entry if any
                manifest.Items.RemoveAll(i => i.CommercialId == commercial.Id);

                // Add new entry
                manifest.Items.Add(TrashManifestItem.FromCommercial(commercial, originalPath, fileSize, DEFAULT_RETENTION_DAYS));
            }

            manifest.LastUpdated = DateTime.Now;
            await SaveManifestAsync(manifestPath, manifest, cancellationToken);
        }

        private async Task UpdateManifestAsync(string trashPath, TrashItem item, bool removeItem, CancellationToken cancellationToken)
        {
            var manifestPath = Path.Combine(trashPath, MANIFEST_FILENAME);
            var manifest = await LoadOrCreateManifestAsync(manifestPath, cancellationToken);

            if (removeItem)
            {
                manifest.Items.RemoveAll(i => i.CommercialId == item.CommercialId);
            }

            manifest.LastUpdated = DateTime.Now;
            await SaveManifestAsync(manifestPath, manifest, cancellationToken);
        }

        private async Task<TrashManifest> LoadOrCreateManifestAsync(string manifestPath, CancellationToken cancellationToken)
        {
            if (File.Exists(manifestPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                    return JsonSerializer.Deserialize<TrashManifest>(json) ?? CreateNewManifest();
                }
                catch
                {
                    return CreateNewManifest();
                }
            }
            return CreateNewManifest();
        }

        private TrashManifest CreateNewManifest()
        {
            return new TrashManifest
            {
                Version = 1,
                Channel = _channel.Name,
                LastUpdated = DateTime.Now,
                Items = new List<TrashManifestItem>()
            };
        }

        private async Task SaveManifestAsync(string manifestPath, TrashManifest manifest, CancellationToken cancellationToken)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(manifest, options);
            await File.WriteAllTextAsync(manifestPath, json, cancellationToken);
        }

        private async Task MarkAsDeletedInDatabaseAsync(int commercialId, CancellationToken cancellationToken)
        {
            var router = DatabaseRouter.Instance;
            var sql = @"UPDATE commercials SET Status = 'Deleted', DeletedDate = @DeletedDate WHERE Id = @Id";
            var parameters = new Dictionary<string, object>
            {
                { "@Id", commercialId },
                { "@DeletedDate", DateTime.Now }
            };

            await router.WriteFanOutAsync(_channelDatabaseName, sql, parameters, cancellationToken);
        }

        private async Task RestoreInDatabaseAsync(int commercialId, CancellationToken cancellationToken)
        {
            var router = DatabaseRouter.Instance;
            var sql = @"UPDATE commercials SET Status = 'Active', DeletedDate = NULL WHERE Id = @Id";
            var parameters = new Dictionary<string, object>
            {
                { "@Id", commercialId }
            };

            await router.WriteFanOutAsync(_channelDatabaseName, sql, parameters, cancellationToken);
        }

        private async Task MarkAsPurgedInDatabaseAsync(int commercialId, CancellationToken cancellationToken)
        {
            var router = DatabaseRouter.Instance;
            var sql = @"UPDATE commercials SET Status = 'Purged', PurgedDate = @PurgedDate WHERE Id = @Id";
            var parameters = new Dictionary<string, object>
            {
                { "@Id", commercialId },
                { "@PurgedDate", DateTime.Now }
            };

            await router.WriteFanOutAsync(_channelDatabaseName, sql, parameters, cancellationToken);
        }

        #endregion
    }

    /// <summary>
    /// Result of a trash operation
    /// </summary>
    public class TrashOperationResult
    {
        public bool Success { get; set; }
        public int SuccessCount { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        public string ErrorSummary => Errors.Count > 0 ? string.Join("\n", Errors) : null;

        public static TrashOperationResult Failed(string error)
        {
            return new TrashOperationResult
            {
                Success = false,
                Errors = new List<string> { error }
            };
        }
    }

    /// <summary>
    /// Statistics about trash contents
    /// </summary>
    public class TrashStatistics
    {
        public int TotalItems { get; set; }
        public int ExpiringToday { get; set; }
        public int ExpiringSoon { get; set; }
        public long TotalSizeBytes { get; set; }

        public string TotalSizeDisplay
        {
            get
            {
                if (TotalSizeBytes < 1024) return $"{TotalSizeBytes} B";
                if (TotalSizeBytes < 1024 * 1024) return $"{TotalSizeBytes / 1024.0:F1} KB";
                if (TotalSizeBytes < 1024 * 1024 * 1024) return $"{TotalSizeBytes / (1024.0 * 1024.0):F2} MB";
                return $"{TotalSizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
            }
        }
    }
}
