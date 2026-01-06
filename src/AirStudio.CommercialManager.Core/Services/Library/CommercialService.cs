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
        /// Load all commercials from the database
        /// </summary>
        public async Task<List<Commercial>> LoadCommercialsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var router = DatabaseRouter.Instance;
                var sql = $@"SELECT Code, Agency, Spot, Title, Duration, Otherinfo, Filename, User, LastUpdate
                            FROM {TABLE_NAME}
                            ORDER BY Spot";

                var result = await router.ReadFirstSuccessAsync(
                    _channelDatabaseName,
                    sql,
                    reader =>
                    {
                        var commercials = new List<Commercial>();
                        while (reader.Read())
                        {
                            commercials.Add(new Commercial
                            {
                                Code = reader.GetInt32OrDefault("Code"),
                                Agency = reader.GetStringOrEmpty("Agency"),
                                Spot = reader.GetStringOrEmpty("Spot"),
                                Title = reader.GetStringOrEmpty("Title"),
                                Duration = reader.GetStringOrEmpty("Duration"),
                                Otherinfo = reader.GetStringOrNull("Otherinfo"),
                                Filename = reader.GetStringOrEmpty("Filename"),
                                User = reader.GetStringOrNull("User"),
                                LastUpdate = reader.GetDateTimeOrDefault("LastUpdate")
                            });
                        }
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
    }
}
