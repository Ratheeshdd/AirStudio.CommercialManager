using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Database;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Core.Services.Library
{
    /// <summary>
    /// Service for managing commercials per channel
    /// </summary>
    public class CommercialService
    {
        private const string TABLE_NAME = "commercials";

        private readonly string _channelDatabaseName;
        private readonly Channel _channel;
        private List<Commercial> _cachedCommercials = new List<Commercial>();
        private DateTime _lastRefresh = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

        public CommercialService(Channel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _channelDatabaseName = channel.DatabaseName;
        }

        /// <summary>
        /// Convert legacy drive letter paths (e.g., X:\Commercial Playlist\...) to UNC paths
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
                    // Extract the relative path after the drive letter (e.g., \Commercial Playlist\file.TAG)
                    var relativePath = path.Substring(2); // Remove "X:"
                    // Combine with UNC root (remove trailing slash from UNC if present)
                    var uncRoot = primaryXRoot.TrimEnd('\\', '/');
                    return uncRoot + relativePath;
                }
            }

            return path;
        }

        /// <summary>
        /// Load all commercials from the database
        /// </summary>
        public async Task<List<Commercial>> LoadCommercialsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var router = DatabaseRouter.Instance;
                // Note: The actual commercials table doesn't have Id, DateIn, Status, DeletedDate columns
                // We use Code as identifier and LastUpdate for date tracking
                var sql = $@"SELECT Code, Agency, Spot, Title, Duration, Otherinfo, Filename, User, LastUpdate
                            FROM {TABLE_NAME}
                            ORDER BY Spot";

                var result = await router.ReadFirstSuccessAsync(
                    _channelDatabaseName,
                    sql,
                    reader =>
                    {
                        var commercials = new List<Commercial>();
                        // Use do-while because reader is already positioned on first row
                        do
                        {
                            // Read Code as string and try to convert to int (database has Code as VARCHAR)
                            var codeStr = reader.GetStringOrEmpty("Code");
                            int codeInt = 0;
                            int.TryParse(codeStr, out codeInt);

                            var commercial = new Commercial
                            {
                                Code = codeInt,
                                Agency = reader.GetStringOrEmpty("Agency"),
                                Spot = reader.GetStringOrEmpty("Spot"),
                                Title = reader.GetStringOrEmpty("Title"),
                                Duration = reader.GetStringOrEmpty("Duration"),
                                Otherinfo = reader.GetStringOrNull("Otherinfo"),
                                Filename = reader.GetStringOrEmpty("Filename"),
                                User = reader.GetStringOrNull("User"),
                                LastUpdate = reader.GetDateTimeOrDefault("LastUpdate")
                            };
                            // Use Code as Id and LastUpdate as DateIn for compatibility
                            commercial.Id = commercial.Code;
                            commercial.DateIn = commercial.LastUpdate;
                            commercial.Status = "Active";
                            commercials.Add(commercial);
                        } while (reader.Read());
                        return commercials;
                    },
                    cancellationToken: cancellationToken);

                if (result.Success && result.Data != null)
                {
                    _cachedCommercials = result.Data;
                    _lastRefresh = DateTime.Now;
                    LogService.Info($"Loaded {_cachedCommercials.Count} commercials from {_channelDatabaseName}");
                    return _cachedCommercials;
                }
                else
                {
                    LogService.Warning($"Failed to load commercials: {result.ErrorMessage}");
                    return _cachedCommercials;
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Exception loading commercials", ex);
                return _cachedCommercials;
            }
        }

        /// <summary>
        /// Get commercials (from cache if valid, otherwise reload)
        /// </summary>
        public async Task<List<Commercial>> GetCommercialsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            if (forceRefresh || DateTime.Now - _lastRefresh > _cacheExpiry || _cachedCommercials.Count == 0)
            {
                return await LoadCommercialsAsync(cancellationToken);
            }
            return _cachedCommercials;
        }

        /// <summary>
        /// Get a commercial by spot name
        /// </summary>
        public async Task<Commercial> GetBySpotAsync(string spotName, CancellationToken cancellationToken = default)
        {
            var commercials = await GetCommercialsAsync(cancellationToken: cancellationToken);
            return commercials.FirstOrDefault(c =>
                string.Equals(c.Spot, spotName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Search commercials by text (spot, title, or agency)
        /// </summary>
        public async Task<List<Commercial>> SearchCommercialsAsync(string searchText, CancellationToken cancellationToken = default)
        {
            var commercials = await GetCommercialsAsync(cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(searchText))
                return commercials;

            var lowerSearch = searchText.ToLowerInvariant();
            return commercials
                .Where(c =>
                    (c.Spot?.ToLowerInvariant().Contains(lowerSearch) == true) ||
                    (c.Title?.ToLowerInvariant().Contains(lowerSearch) == true) ||
                    (c.Agency?.ToLowerInvariant().Contains(lowerSearch) == true))
                .OrderBy(c => c.Spot)
                .ToList();
        }

        /// <summary>
        /// Add a new commercial (fan-out write to all servers)
        /// </summary>
        public async Task<DbOperationResult> AddCommercialAsync(Commercial commercial, string username, CancellationToken cancellationToken = default)
        {
            if (!commercial.IsValid)
            {
                return DbOperationResult.Failed(_channelDatabaseName, "Commercial data is invalid (Code, Spot, and Filename are required)");
            }

            try
            {
                var router = DatabaseRouter.Instance;
                var sql = $@"INSERT INTO {TABLE_NAME} (Code, Agency, Spot, Title, Duration, Otherinfo, Filename, User, LastUpdate)
                            VALUES (@Code, @Agency, @Spot, @Title, @Duration, @Otherinfo, @Filename, @User, NOW())";

                var parameters = new Dictionary<string, object>
                {
                    { "@Code", commercial.Code },
                    { "@Agency", commercial.Agency ?? (object)DBNull.Value },
                    { "@Spot", commercial.Spot },
                    { "@Title", commercial.Title ?? (object)DBNull.Value },
                    { "@Duration", commercial.Duration ?? (object)DBNull.Value },
                    { "@Otherinfo", commercial.Otherinfo ?? (object)DBNull.Value },
                    { "@Filename", commercial.Filename },
                    { "@User", username ?? (object)DBNull.Value }
                };

                var result = await router.WriteFanOutAsync(_channelDatabaseName, sql, parameters, cancellationToken);

                if (result.AnySucceeded)
                {
                    _lastRefresh = DateTime.MinValue;
                    LogService.Info($"Added commercial '{commercial.Spot}' to {_channelDatabaseName}");
                }

                return result.AnySucceeded
                    ? DbOperationResult.Succeeded(_channelDatabaseName, result.SuccessCount)
                    : DbOperationResult.Failed(_channelDatabaseName, result.GetSummary());
            }
            catch (Exception ex)
            {
                LogService.Error("Exception adding commercial", ex);
                return DbOperationResult.Failed(_channelDatabaseName, ex.Message, ex);
            }
        }

        /// <summary>
        /// Update an existing commercial (fan-out write, self-healing)
        /// Uses Spot as the key since there's no unique ID
        /// </summary>
        public async Task<DbOperationResult> UpdateCommercialAsync(Commercial commercial, string originalSpot, string username, CancellationToken cancellationToken = default)
        {
            if (!commercial.IsValid)
            {
                return DbOperationResult.Failed(_channelDatabaseName, "Commercial data is invalid");
            }

            try
            {
                var router = DatabaseRouter.Instance;

                var updateSql = $@"UPDATE {TABLE_NAME} SET
                                  Code = @Code,
                                  Agency = @Agency,
                                  Spot = @Spot,
                                  Title = @Title,
                                  Duration = @Duration,
                                  Otherinfo = @Otherinfo,
                                  Filename = @Filename,
                                  User = @User,
                                  LastUpdate = NOW()
                                  WHERE Spot = @OriginalSpot";

                var insertSql = $@"INSERT INTO {TABLE_NAME} (Code, Agency, Spot, Title, Duration, Otherinfo, Filename, User, LastUpdate)
                                  VALUES (@Code, @Agency, @Spot, @Title, @Duration, @Otherinfo, @Filename, @User, NOW())";

                var updateParams = new Dictionary<string, object>
                {
                    { "@Code", commercial.Code },
                    { "@Agency", commercial.Agency ?? (object)DBNull.Value },
                    { "@Spot", commercial.Spot },
                    { "@Title", commercial.Title ?? (object)DBNull.Value },
                    { "@Duration", commercial.Duration ?? (object)DBNull.Value },
                    { "@Otherinfo", commercial.Otherinfo ?? (object)DBNull.Value },
                    { "@Filename", commercial.Filename },
                    { "@User", username ?? (object)DBNull.Value },
                    { "@OriginalSpot", originalSpot }
                };

                var insertParams = new Dictionary<string, object>
                {
                    { "@Code", commercial.Code },
                    { "@Agency", commercial.Agency ?? (object)DBNull.Value },
                    { "@Spot", commercial.Spot },
                    { "@Title", commercial.Title ?? (object)DBNull.Value },
                    { "@Duration", commercial.Duration ?? (object)DBNull.Value },
                    { "@Otherinfo", commercial.Otherinfo ?? (object)DBNull.Value },
                    { "@Filename", commercial.Filename },
                    { "@User", username ?? (object)DBNull.Value }
                };

                var result = await router.WriteSelfHealingAsync(
                    _channelDatabaseName, updateSql, insertSql, updateParams, insertParams, cancellationToken);

                if (result.AnySucceeded)
                {
                    _lastRefresh = DateTime.MinValue;
                    LogService.Info($"Updated commercial '{commercial.Spot}' in {_channelDatabaseName}");
                }

                return result.AnySucceeded
                    ? DbOperationResult.Succeeded(_channelDatabaseName, result.SuccessCount)
                    : DbOperationResult.Failed(_channelDatabaseName, result.GetSummary());
            }
            catch (Exception ex)
            {
                LogService.Error("Exception updating commercial", ex);
                return DbOperationResult.Failed(_channelDatabaseName, ex.Message, ex);
            }
        }

        /// <summary>
        /// Delete a commercial (fan-out write to all servers)
        /// </summary>
        public async Task<DbOperationResult> DeleteCommercialAsync(string spotName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(spotName))
            {
                return DbOperationResult.Failed(_channelDatabaseName, "Spot name is required");
            }

            try
            {
                var router = DatabaseRouter.Instance;
                var sql = $"DELETE FROM {TABLE_NAME} WHERE Spot = @Spot";
                var parameters = new Dictionary<string, object> { { "@Spot", spotName } };

                var result = await router.WriteFanOutAsync(_channelDatabaseName, sql, parameters, cancellationToken);

                if (result.AnySucceeded)
                {
                    _lastRefresh = DateTime.MinValue;
                    LogService.Info($"Deleted commercial '{spotName}' from {_channelDatabaseName}");
                }

                return result.AnySucceeded
                    ? DbOperationResult.Succeeded(_channelDatabaseName, result.SuccessCount)
                    : DbOperationResult.Failed(_channelDatabaseName, result.GetSummary());
            }
            catch (Exception ex)
            {
                LogService.Error("Exception deleting commercial", ex);
                return DbOperationResult.Failed(_channelDatabaseName, ex.Message, ex);
            }
        }

        /// <summary>
        /// Invalidate the cache
        /// </summary>
        public void InvalidateCache()
        {
            _lastRefresh = DateTime.MinValue;
        }

        /// <summary>
        /// Load all scheduled commercials from the playlist table
        /// Filters by ProgType='COMMERCIALS'
        /// </summary>
        public async Task<List<ScheduledCommercial>> LoadScheduledCommercialsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var router = DatabaseRouter.Instance;

                var sql = @"SELECT Id, TxDate, TxTime, Programme, Title, Duration, Validity,
                                   MainPath, UserName, MobileNo, LoginUser, LastUpdate
                           FROM playlist
                           WHERE ProgType = 'COMMERCIALS'
                           ORDER BY TxDate ASC, TxTime ASC";

                var result = await router.ReadFirstSuccessAsync(
                    _channelDatabaseName,
                    sql,
                    reader =>
                    {
                        var schedules = new List<ScheduledCommercial>();
                        do
                        {
                            var rawPath = reader.GetStringOrEmpty("MainPath");
                            schedules.Add(new ScheduledCommercial
                            {
                                Id = reader.GetValueOrDefault<int>("Id"),
                                TxDate = reader.GetDateTimeOrDefault("TxDate"),
                                TxTime = reader.GetStringOrEmpty("TxTime"),
                                CapsuleName = reader.GetStringOrEmpty("Programme"),
                                Title = reader.GetStringOrEmpty("Title"),
                                Duration = reader.GetStringOrEmpty("Duration"),
                                ToDate = reader.GetDateTimeOrDefault("Validity"),
                                TagFilePath = ConvertToUncPath(rawPath),
                                UserName = reader.GetStringOrNull("UserName"),
                                MobileNo = reader.GetStringOrNull("MobileNo"),
                                LoginUser = reader.GetStringOrNull("LoginUser"),
                                LastUpdate = reader.GetDateTimeOrDefault("LastUpdate")
                            });
                        } while (reader.Read());
                        return schedules;
                    },
                    cancellationToken: cancellationToken);

                if (result.Success)
                {
                    // Data will be null if there are no rows (empty result set)
                    var data = result.Data ?? new List<ScheduledCommercial>();
                    LogService.Info($"Loaded {data.Count} scheduled commercials from {_channelDatabaseName}");
                    return data;
                }
                else
                {
                    LogService.Warning($"Failed to load scheduled commercials: {result.ErrorMessage}");
                    return new List<ScheduledCommercial>();
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Exception loading scheduled commercials", ex);
                return new List<ScheduledCommercial>();
            }
        }

        /// <summary>
        /// Delete a scheduled commercial from the playlist table
        /// </summary>
        public async Task<DbOperationResult> DeleteScheduledCommercialAsync(int id, CancellationToken cancellationToken = default)
        {
            try
            {
                var router = DatabaseRouter.Instance;
                var sql = "DELETE FROM playlist WHERE Id = @Id AND ProgType = 'COMMERCIALS'";
                var parameters = new Dictionary<string, object> { { "@Id", id } };

                var result = await router.WriteFanOutAsync(_channelDatabaseName, sql, parameters, cancellationToken);

                if (result.AnySucceeded)
                {
                    LogService.Info($"Deleted scheduled commercial ID={id} from {_channelDatabaseName}");
                }

                return result.AnySucceeded
                    ? DbOperationResult.Succeeded(_channelDatabaseName, result.SuccessCount)
                    : DbOperationResult.Failed(_channelDatabaseName, result.GetSummary());
            }
            catch (Exception ex)
            {
                LogService.Error("Exception deleting scheduled commercial", ex);
                return DbOperationResult.Failed(_channelDatabaseName, ex.Message, ex);
            }
        }

        /// <summary>
        /// Get all commercials for a specific year (for cleanup window)
        /// </summary>
        public async Task<List<Commercial>> GetCommercialsByYearAsync(int? year = null, bool includeDeleted = false, CancellationToken cancellationToken = default)
        {
            try
            {
                var router = DatabaseRouter.Instance;

                var whereClause = year.HasValue
                    ? "WHERE YEAR(LastUpdate) = @Year"
                    : "WHERE 1=1";

                var sql = $@"SELECT Code, Agency, Spot, Title, Duration, Otherinfo, Filename, User, LastUpdate
                            FROM {TABLE_NAME}
                            {whereClause}
                            ORDER BY LastUpdate DESC, Spot";

                var parameters = year.HasValue
                    ? new Dictionary<string, object> { { "Year", year.Value } }
                    : null;

                var result = await router.ReadFirstSuccessAsync(
                    _channelDatabaseName,
                    sql,
                    reader =>
                    {
                        var commercials = new List<Commercial>();
                        do
                        {
                            // Read Code as string and try to convert to int (database has Code as VARCHAR)
                            var codeStr = reader.GetStringOrEmpty("Code");
                            int codeInt = 0;
                            int.TryParse(codeStr, out codeInt);

                            var commercial = new Commercial
                            {
                                Code = codeInt,
                                Agency = reader.GetStringOrEmpty("Agency"),
                                Spot = reader.GetStringOrEmpty("Spot"),
                                Title = reader.GetStringOrEmpty("Title"),
                                Duration = reader.GetStringOrEmpty("Duration"),
                                Otherinfo = reader.GetStringOrNull("Otherinfo"),
                                Filename = reader.GetStringOrEmpty("Filename"),
                                User = reader.GetStringOrNull("User"),
                                LastUpdate = reader.GetDateTimeOrDefault("LastUpdate")
                            };
                            commercial.Id = commercial.Code;
                            commercial.DateIn = commercial.LastUpdate;
                            commercial.Status = "Active";
                            commercials.Add(commercial);
                        } while (reader.Read());
                        return commercials;
                    },
                    parameters,
                    cancellationToken);

                if (result.Success && result.Data != null)
                {
                    return result.Data;
                }

                return new List<Commercial>();
            }
            catch (Exception ex)
            {
                LogService.Error($"Exception loading commercials by year {year}", ex);
                return new List<Commercial>();
            }
        }

        /// <summary>
        /// Get available years that have commercials
        /// </summary>
        public async Task<List<int>> GetAvailableYearsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var router = DatabaseRouter.Instance;
                var sql = $@"SELECT DISTINCT YEAR(LastUpdate) as Year
                            FROM {TABLE_NAME}
                            ORDER BY Year DESC";

                var result = await router.ReadFirstSuccessAsync(
                    _channelDatabaseName,
                    sql,
                    reader =>
                    {
                        var years = new List<int>();
                        do
                        {
                            var year = reader.GetValueOrDefault<int>("Year");
                            if (year > 1900)
                            {
                                years.Add(year);
                            }
                        } while (reader.Read());
                        return years;
                    },
                    cancellationToken: cancellationToken);

                if (result.Success && result.Data != null)
                {
                    return result.Data;
                }

                return new List<int>();
            }
            catch (Exception ex)
            {
                LogService.Error("Exception getting available years", ex);
                return new List<int>();
            }
        }
    }
}
