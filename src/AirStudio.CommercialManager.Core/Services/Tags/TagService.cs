using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Database;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Core.Services.Tags
{
    /// <summary>
    /// Service for TAG file generation, parsing, and management
    /// </summary>
    public class TagService
    {
        private const string TAG_FOLDER = "Commercial Playlist";
        private const string PLAYLIST_TABLE = "playlist";

        private readonly Channel _channel;
        private readonly string _channelDatabaseName;

        public TagService(Channel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _channelDatabaseName = channel.DatabaseName;
        }

        #region File Operations

        /// <summary>
        /// Get the TAG folder path for the primary X target
        /// </summary>
        public string GetPrimaryTagFolder()
        {
            var primaryXRoot = _channel.PrimaryXRoot;
            if (string.IsNullOrEmpty(primaryXRoot))
                return null;

            return Path.Combine(primaryXRoot, TAG_FOLDER);
        }

        /// <summary>
        /// Get all TAG folder paths across X targets
        /// </summary>
        public List<string> GetAllTagFolders()
        {
            var folders = new List<string>();

            foreach (var xRoot in _channel.GetAccessibleTargets())
            {
                var tagFolder = Path.Combine(xRoot, TAG_FOLDER);
                if (Directory.Exists(tagFolder))
                {
                    folders.Add(tagFolder);
                }
            }

            return folders;
        }

        /// <summary>
        /// List all TAG files in the primary TAG folder
        /// </summary>
        public List<string> ListTagFiles()
        {
            var tagFolder = GetPrimaryTagFolder();
            LogService.Info($"ListTagFiles: PrimaryXRoot='{_channel.PrimaryXRoot}', TagFolder='{tagFolder}'");

            if (string.IsNullOrEmpty(tagFolder))
            {
                LogService.Warning("ListTagFiles: TagFolder is null or empty");
                return new List<string>();
            }

            if (!Directory.Exists(tagFolder))
            {
                LogService.Warning($"ListTagFiles: Directory does not exist: {tagFolder}");
                return new List<string>();
            }

            try
            {
                var files = Directory.GetFiles(tagFolder, "*.TAG", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .ToList();
                LogService.Info($"ListTagFiles: Found {files.Count} TAG files in {tagFolder}");
                return files;
            }
            catch (Exception ex)
            {
                LogService.Error($"Failed to list TAG files from {tagFolder}", ex);
                return new List<string>();
            }
        }

        /// <summary>
        /// Load and parse a TAG file
        /// </summary>
        public TagFile LoadTagFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                LogService.Warning($"TAG file not found: {filePath}");
                return null;
            }

            try
            {
                var content = File.ReadAllText(filePath);
                var tagFile = TagFile.Parse(content, filePath);

                // Convert legacy drive letter paths to UNC paths in all entries
                foreach (var entry in tagFile.Entries)
                {
                    entry.AudioPath = ConvertToUncPath(entry.AudioPath);
                }

                LogService.Info($"Loaded TAG file: {tagFile.FileName} ({tagFile.CutCount} cuts)");
                return tagFile;
            }
            catch (Exception ex)
            {
                LogService.Error($"Failed to load TAG file: {filePath}", ex);
                return null;
            }
        }

        /// <summary>
        /// Convert legacy drive letter paths (e.g., X:\COMMERCIALS\...) to UNC paths
        /// </summary>
        private string ConvertToUncPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Check if path starts with a drive letter (e.g., X:\, Y:\)
            if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
            {
                var primaryXRoot = _channel?.PrimaryXRoot;
                if (!string.IsNullOrEmpty(primaryXRoot))
                {
                    // Extract the relative path after the drive letter (e.g., \COMMERCIALS\file.wav)
                    var relativePath = path.Substring(2); // Remove "X:"
                    // Combine with UNC root (remove trailing slash from UNC if present)
                    var uncRoot = primaryXRoot.TrimEnd('\\', '/');
                    return uncRoot + relativePath;
                }
            }

            return path;
        }

        /// <summary>
        /// Save a TAG file to all X targets (fan-out)
        /// </summary>
        public async Task<TagSaveResult> SaveTagFileAsync(
            TagFile tagFile,
            CancellationToken cancellationToken = default)
        {
            var result = new TagSaveResult();
            var fileName = tagFile.GenerateFileName();
            var content = tagFile.GenerateContent();

            var tasks = new List<Task>();

            foreach (var xRoot in _channel.GetAccessibleTargets())
            {
                var tagFolder = Path.Combine(xRoot, TAG_FOLDER);
                var fullPath = Path.Combine(tagFolder, fileName);

                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        // Ensure folder exists
                        if (!Directory.Exists(tagFolder))
                        {
                            Directory.CreateDirectory(tagFolder);
                        }

                        // Write atomically (temp file + move)
                        var tempPath = fullPath + ".tmp";
                        File.WriteAllText(tempPath, content);

                        if (File.Exists(fullPath))
                        {
                            File.Delete(fullPath);
                        }
                        File.Move(tempPath, fullPath);

                        lock (result)
                        {
                            result.SuccessfulPaths.Add(fullPath);
                        }

                        LogService.Info($"Saved TAG file to: {fullPath}");
                    }
                    catch (Exception ex)
                    {
                        lock (result)
                        {
                            result.FailedPaths.Add((fullPath, ex.Message));
                        }
                        LogService.Error($"Failed to save TAG to: {fullPath}", ex);
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);

            // Set the primary path for database
            if (result.SuccessfulPaths.Count > 0)
            {
                var primaryTagFolder = GetPrimaryTagFolder();
                result.PrimaryPath = Path.Combine(primaryTagFolder, fileName);
                tagFile.FilePath = result.PrimaryPath;
            }

            return result;
        }

        /// <summary>
        /// Rename a TAG file across all X targets
        /// </summary>
        public async Task<TagSaveResult> RenameTagFileAsync(
            string oldPath,
            TagFile tagFile,
            CancellationToken cancellationToken = default)
        {
            var oldFileName = Path.GetFileName(oldPath);
            var newFileName = tagFile.GenerateFileName();

            if (oldFileName.Equals(newFileName, StringComparison.OrdinalIgnoreCase))
            {
                // No rename needed, just update content
                return await SaveTagFileAsync(tagFile, cancellationToken);
            }

            var result = new TagSaveResult();
            var content = tagFile.GenerateContent();
            var tasks = new List<Task>();

            foreach (var xRoot in _channel.GetAccessibleTargets())
            {
                var tagFolder = Path.Combine(xRoot, TAG_FOLDER);
                var oldFullPath = Path.Combine(tagFolder, oldFileName);
                var newFullPath = Path.Combine(tagFolder, newFileName);

                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        // Write new file
                        var tempPath = newFullPath + ".tmp";
                        File.WriteAllText(tempPath, content);

                        if (File.Exists(newFullPath))
                        {
                            File.Delete(newFullPath);
                        }
                        File.Move(tempPath, newFullPath);

                        // Delete old file
                        if (File.Exists(oldFullPath))
                        {
                            File.Delete(oldFullPath);
                        }

                        lock (result)
                        {
                            result.SuccessfulPaths.Add(newFullPath);
                        }

                        LogService.Info($"Renamed TAG file: {oldFileName} -> {newFileName} at {xRoot}");
                    }
                    catch (Exception ex)
                    {
                        lock (result)
                        {
                            result.FailedPaths.Add((newFullPath, ex.Message));
                        }
                        LogService.Error($"Failed to rename TAG at: {xRoot}", ex);
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);

            if (result.SuccessfulPaths.Count > 0)
            {
                var primaryTagFolder = GetPrimaryTagFolder();
                result.PrimaryPath = Path.Combine(primaryTagFolder, newFileName);
                tagFile.FilePath = result.PrimaryPath;
            }

            return result;
        }

        /// <summary>
        /// Delete a TAG file from all X targets
        /// </summary>
        public async Task DeleteTagFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var fileName = Path.GetFileName(filePath);
            var tasks = new List<Task>();

            foreach (var xRoot in _channel.GetAccessibleTargets())
            {
                var tagFolder = Path.Combine(xRoot, TAG_FOLDER);
                var fullPath = Path.Combine(tagFolder, fileName);

                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        if (File.Exists(fullPath))
                        {
                            File.Delete(fullPath);
                            LogService.Info($"Deleted TAG file: {fullPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Error($"Failed to delete TAG: {fullPath}", ex);
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Replicate a TAG file to targets where it's missing
        /// </summary>
        public async Task<int> ReplicateMissingTagAsync(
            string primaryPath,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(primaryPath))
            {
                LogService.Warning($"Cannot replicate, source TAG not found: {primaryPath}");
                return 0;
            }

            var fileName = Path.GetFileName(primaryPath);
            var content = await Task.Run(() => File.ReadAllText(primaryPath), cancellationToken);
            var replicated = 0;
            var lockObj = new object();
            var tasks = new List<Task>();

            foreach (var xRoot in _channel.GetAccessibleTargets())
            {
                var tagFolder = Path.Combine(xRoot, TAG_FOLDER);
                var targetPath = Path.Combine(tagFolder, fileName);

                if (!File.Exists(targetPath))
                {
                    tasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            if (!Directory.Exists(tagFolder))
                            {
                                Directory.CreateDirectory(tagFolder);
                            }

                            File.WriteAllText(targetPath, content);
                            lock (lockObj)
                            {
                                replicated++;
                            }
                            LogService.Info($"Replicated TAG to: {targetPath}");
                        }
                        catch (Exception ex)
                        {
                            LogService.Error($"Failed to replicate TAG to: {targetPath}", ex);
                        }
                    }, cancellationToken));
                }
            }

            await Task.WhenAll(tasks);
            return replicated;
        }

        #endregion

        #region Playlist Database Operations

        /// <summary>
        /// Insert or update a playlist row for the TAG file
        /// </summary>
        public async Task<DbOperationResult> SavePlaylistRowAsync(
            TagFile tagFile,
            string userName,
            string mobileNo,
            CancellationToken cancellationToken = default)
        {
            var router = DatabaseRouter.Instance;

            var updateSql = $@"UPDATE {PLAYLIST_TABLE} SET
                              Programme = @Programme,
                              Title = @Title,
                              Duration = @Duration,
                              StopTime = @StopTime,
                              MainPath = @MainPath,
                              LoginUser = @LoginUser,
                              UserName = @UserName,
                              MobileNo = @MobileNo,
                              LastUpdate = NOW()
                              WHERE ProgType = 'COMMERCIALS' AND TxDate = @TxDate AND TxTime = @TxTime AND MainPath = @OldMainPath";

            var insertSql = $@"INSERT INTO {PLAYLIST_TABLE}
                              (Mode, TxTime, TxDate, Validity, Programme, Title, Duration, StopTime, ProgType, MainPath, LoginUser, UserName, MobileNo, LastUpdate)
                              VALUES
                              (2, @TxTime, @TxDate, @Validity, @Programme, @Title, @Duration, @StopTime, 'COMMERCIALS', @MainPath, @LoginUser, @UserName, @MobileNo, NOW())";

            var loginUser = Environment.UserDomainName + "\\" + Environment.UserName;

            var updateParams = new Dictionary<string, object>
            {
                { "@Programme", tagFile.CapsuleName },
                { "@Title", tagFile.FirstCutSpotName },
                { "@Duration", tagFile.TotalDurationFormatted },
                { "@StopTime", tagFile.TotalDuration.TotalSeconds },
                { "@MainPath", tagFile.FilePath },
                { "@OldMainPath", tagFile.FilePath }, // Same for update, different if rename
                { "@LoginUser", loginUser },
                { "@UserName", userName ?? "" },
                { "@MobileNo", mobileNo ?? "" },
                { "@TxDate", tagFile.TxDate.Date },
                { "@TxTime", tagFile.TxTime.ToString(@"hh\:mm\:ss") }
            };

            var insertParams = new Dictionary<string, object>
            {
                { "@TxTime", tagFile.TxTime.ToString(@"hh\:mm\:ss") },
                { "@TxDate", tagFile.TxDate.Date },
                { "@Validity", tagFile.ToDate.Date },
                { "@Programme", tagFile.CapsuleName },
                { "@Title", tagFile.FirstCutSpotName },
                { "@Duration", tagFile.TotalDurationFormatted },
                { "@StopTime", tagFile.TotalDuration.TotalSeconds },
                { "@MainPath", tagFile.FilePath },
                { "@LoginUser", loginUser },
                { "@UserName", userName ?? "" },
                { "@MobileNo", mobileNo ?? "" }
            };

            try
            {
                var result = await router.WriteSelfHealingAsync(
                    _channelDatabaseName,
                    updateSql,
                    insertSql,
                    updateParams,
                    insertParams,
                    cancellationToken);

                if (result.AnySucceeded)
                {
                    LogService.Info($"Saved playlist row for TAG: {tagFile.FileName}");
                    return DbOperationResult.Succeeded(_channelDatabaseName, result.SuccessCount);
                }
                else
                {
                    return DbOperationResult.Failed(_channelDatabaseName, result.GetSummary());
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to save playlist row", ex);
                return DbOperationResult.Failed(_channelDatabaseName, ex.Message, ex);
            }
        }

        /// <summary>
        /// Update playlist row when TAG file is renamed
        /// </summary>
        public async Task<DbOperationResult> UpdatePlaylistPathAsync(
            string oldPath,
            TagFile tagFile,
            string userName,
            string mobileNo,
            CancellationToken cancellationToken = default)
        {
            var router = DatabaseRouter.Instance;

            // First try to update the existing row with old path
            var updateSql = $@"UPDATE {PLAYLIST_TABLE} SET
                              TxDate = @NewTxDate,
                              TxTime = @NewTxTime,
                              Validity = @Validity,
                              Programme = @Programme,
                              Title = @Title,
                              Duration = @Duration,
                              StopTime = @StopTime,
                              MainPath = @NewMainPath,
                              LoginUser = @LoginUser,
                              UserName = @UserName,
                              MobileNo = @MobileNo,
                              LastUpdate = NOW()
                              WHERE ProgType = 'COMMERCIALS' AND MainPath = @OldMainPath";

            var loginUser = Environment.UserDomainName + "\\" + Environment.UserName;

            var updateParams = new Dictionary<string, object>
            {
                { "@NewTxDate", tagFile.TxDate.Date },
                { "@NewTxTime", tagFile.TxTime.ToString(@"hh\:mm\:ss") },
                { "@Validity", tagFile.ToDate.Date },
                { "@Programme", tagFile.CapsuleName },
                { "@Title", tagFile.FirstCutSpotName },
                { "@Duration", tagFile.TotalDurationFormatted },
                { "@StopTime", tagFile.TotalDuration.TotalSeconds },
                { "@NewMainPath", tagFile.FilePath },
                { "@OldMainPath", oldPath },
                { "@LoginUser", loginUser },
                { "@UserName", userName ?? "" },
                { "@MobileNo", mobileNo ?? "" }
            };

            try
            {
                var result = await router.WriteFanOutAsync(
                    _channelDatabaseName,
                    updateSql,
                    updateParams,
                    cancellationToken);

                if (result.AnySucceeded)
                {
                    LogService.Info($"Updated playlist path: {Path.GetFileName(oldPath)} -> {tagFile.FileName}");
                    return DbOperationResult.Succeeded(_channelDatabaseName, result.SuccessCount);
                }
                else
                {
                    // If no rows updated, insert new row
                    return await SavePlaylistRowAsync(tagFile, userName, mobileNo, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to update playlist path", ex);
                return DbOperationResult.Failed(_channelDatabaseName, ex.Message, ex);
            }
        }

        /// <summary>
        /// Delete playlist row for a TAG file
        /// </summary>
        public async Task<DbOperationResult> DeletePlaylistRowAsync(
            string tagPath,
            CancellationToken cancellationToken = default)
        {
            var router = DatabaseRouter.Instance;

            var sql = $"DELETE FROM {PLAYLIST_TABLE} WHERE ProgType = 'COMMERCIALS' AND MainPath = @MainPath";
            var parameters = new Dictionary<string, object>
            {
                { "@MainPath", tagPath }
            };

            try
            {
                var result = await router.WriteFanOutAsync(_channelDatabaseName, sql, parameters, cancellationToken);

                if (result.AnySucceeded)
                {
                    LogService.Info($"Deleted playlist row for TAG: {Path.GetFileName(tagPath)}");
                    return DbOperationResult.Succeeded(_channelDatabaseName, result.SuccessCount);
                }
                else
                {
                    return DbOperationResult.Failed(_channelDatabaseName, result.GetSummary());
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to delete playlist row", ex);
                return DbOperationResult.Failed(_channelDatabaseName, ex.Message, ex);
            }
        }

        /// <summary>
        /// Check if a playlist row exists for the given schedule
        /// </summary>
        public async Task<bool> PlaylistRowExistsAsync(
            DateTime txDate,
            TimeSpan txTime,
            CancellationToken cancellationToken = default)
        {
            var router = DatabaseRouter.Instance;

            var sql = $@"SELECT COUNT(*) FROM {PLAYLIST_TABLE}
                        WHERE ProgType = 'COMMERCIALS' AND TxDate = @TxDate AND TxTime = @TxTime";

            var result = await router.ReadFirstSuccessAsync(
                _channelDatabaseName,
                sql,
                reader =>
                {
                    if (reader.Read())
                    {
                        return reader.GetInt32(0) > 0;
                    }
                    return false;
                },
                new Dictionary<string, object>
                {
                    { "@TxDate", txDate.Date },
                    { "@TxTime", txTime.ToString(@"hh\:mm\:ss") }
                },
                cancellationToken);

            return result.Success && result.Data;
        }

        #endregion
    }

    /// <summary>
    /// Result of a TAG file save operation
    /// </summary>
    public class TagSaveResult
    {
        public List<string> SuccessfulPaths { get; } = new List<string>();
        public List<(string Path, string Error)> FailedPaths { get; } = new List<(string, string)>();
        public string PrimaryPath { get; set; }

        public bool AnySucceeded => SuccessfulPaths.Count > 0;
        public bool AllSucceeded => FailedPaths.Count == 0 && SuccessfulPaths.Count > 0;
        public int SuccessCount => SuccessfulPaths.Count;
        public int FailCount => FailedPaths.Count;

        public string GetSummary()
        {
            if (AllSucceeded)
                return $"Saved to {SuccessCount} target(s)";

            if (!AnySucceeded)
                return $"Failed to save to all targets: {string.Join("; ", FailedPaths.Select(f => f.Error))}";

            return $"Saved to {SuccessCount}, failed on {FailCount} target(s)";
        }
    }
}
