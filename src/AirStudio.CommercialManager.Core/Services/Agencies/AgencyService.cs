using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Database;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Core.Services.Agencies
{
    /// <summary>
    /// Service for managing agencies per channel
    /// </summary>
    public class AgencyService
    {
        private const string TABLE_NAME = "agency";

        private readonly string _channelDatabaseName;
        private List<Agency> _cachedAgencies = new List<Agency>();
        private DateTime _lastRefresh = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

        public AgencyService(Channel channel)
        {
            if (channel == null)
                throw new ArgumentNullException(nameof(channel));

            _channelDatabaseName = channel.DatabaseName;
        }

        public AgencyService(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
                throw new ArgumentNullException(nameof(channelName));

            _channelDatabaseName = $"air_{channelName}";
        }

        /// <summary>
        /// Load all agencies from the database
        /// </summary>
        public async Task<List<Agency>> LoadAgenciesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var router = DatabaseRouter.Instance;
                var sql = $"SELECT Code, AgencyName, Address, PIN, Phone, Email FROM {TABLE_NAME} ORDER BY AgencyName";

                var result = await router.ReadFirstSuccessAsync(
                    _channelDatabaseName,
                    sql,
                    reader =>
                    {
                        var agencies = new List<Agency>();
                        // Use do-while because reader is already positioned on first row
                        do
                        {
                            // Read Code as string and try to convert to int (database has Code as VARCHAR)
                            var codeStr = reader.GetStringOrEmpty("Code");
                            int codeInt = 0;
                            int.TryParse(codeStr, out codeInt);

                            agencies.Add(new Agency
                            {
                                Code = codeInt,
                                AgencyName = reader.GetStringOrEmpty("AgencyName"),
                                Address = reader.GetStringOrNull("Address"),
                                PIN = reader.GetStringOrNull("PIN"),
                                Phone = reader.GetStringOrNull("Phone"),
                                Email = reader.GetStringOrNull("Email")
                            });
                        } while (reader.Read());
                        return agencies;
                    },
                    cancellationToken: cancellationToken);

                if (result.Success && result.Data != null)
                {
                    _cachedAgencies = result.Data;
                    _lastRefresh = DateTime.Now;
                    LogService.Info($"Loaded {_cachedAgencies.Count} agencies from {_channelDatabaseName}");
                    return _cachedAgencies;
                }
                else
                {
                    LogService.Warning($"Failed to load agencies: {result.ErrorMessage}");
                    return _cachedAgencies;
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Exception loading agencies", ex);
                return _cachedAgencies;
            }
        }

        /// <summary>
        /// Get agencies (from cache if valid, otherwise reload)
        /// </summary>
        public async Task<List<Agency>> GetAgenciesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            if (forceRefresh || DateTime.Now - _lastRefresh > _cacheExpiry || _cachedAgencies.Count == 0)
            {
                return await LoadAgenciesAsync(cancellationToken);
            }
            return _cachedAgencies;
        }

        /// <summary>
        /// Get agency by code
        /// </summary>
        public async Task<Agency> GetAgencyByCodeAsync(int code, CancellationToken cancellationToken = default)
        {
            var agencies = await GetAgenciesAsync(cancellationToken: cancellationToken);
            return agencies.FirstOrDefault(a => a.Code == code);
        }

        /// <summary>
        /// Search agencies by name (for typeahead)
        /// </summary>
        public async Task<List<Agency>> SearchAgenciesAsync(string searchText, CancellationToken cancellationToken = default)
        {
            var agencies = await GetAgenciesAsync(cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(searchText))
                return agencies;

            var lowerSearch = searchText.ToLowerInvariant();
            return agencies
                .Where(a => a.SearchText.Contains(lowerSearch))
                .OrderBy(a => !a.SearchText.StartsWith(lowerSearch)) // Prioritize starts-with matches
                .ThenBy(a => a.AgencyName)
                .ToList();
        }

        /// <summary>
        /// Add a new agency (fan-out write to all servers)
        /// </summary>
        public async Task<DbOperationResult> AddAgencyAsync(Agency agency, CancellationToken cancellationToken = default)
        {
            if (!agency.IsValid)
            {
                return DbOperationResult.Failed(_channelDatabaseName, "Agency name is required");
            }

            try
            {
                var router = DatabaseRouter.Instance;
                var sql = $@"INSERT INTO {TABLE_NAME} (AgencyName, Address, PIN, Phone, Email)
                            VALUES (@AgencyName, @Address, @PIN, @Phone, @Email)";

                var parameters = new Dictionary<string, object>
                {
                    { "@AgencyName", agency.AgencyName },
                    { "@Address", agency.Address ?? (object)DBNull.Value },
                    { "@PIN", agency.PIN ?? (object)DBNull.Value },
                    { "@Phone", agency.Phone ?? (object)DBNull.Value },
                    { "@Email", agency.Email ?? (object)DBNull.Value }
                };

                var result = await router.WriteFanOutAsync(_channelDatabaseName, sql, parameters, cancellationToken);

                if (result.AnySucceeded)
                {
                    // Get the last insert ID from the first successful result
                    var firstSuccess = result.Results.FirstOrDefault(r => r.Success);
                    if (firstSuccess != null)
                    {
                        agency.Code = (int)firstSuccess.LastInsertId;
                    }

                    // Invalidate cache
                    _lastRefresh = DateTime.MinValue;
                    LogService.Info($"Added agency '{agency.AgencyName}' to {_channelDatabaseName}");
                }

                return result.AnySucceeded
                    ? DbOperationResult.Succeeded(_channelDatabaseName, result.SuccessCount)
                    : DbOperationResult.Failed(_channelDatabaseName, result.GetSummary());
            }
            catch (Exception ex)
            {
                LogService.Error("Exception adding agency", ex);
                return DbOperationResult.Failed(_channelDatabaseName, ex.Message, ex);
            }
        }

        /// <summary>
        /// Update an existing agency (fan-out write to all servers)
        /// </summary>
        public async Task<DbOperationResult> UpdateAgencyAsync(Agency agency, CancellationToken cancellationToken = default)
        {
            if (agency.Code <= 0)
            {
                return DbOperationResult.Failed(_channelDatabaseName, "Invalid agency code");
            }

            if (!agency.IsValid)
            {
                return DbOperationResult.Failed(_channelDatabaseName, "Agency name is required");
            }

            try
            {
                var router = DatabaseRouter.Instance;
                var sql = $@"UPDATE {TABLE_NAME} SET
                            AgencyName = @AgencyName,
                            Address = @Address,
                            PIN = @PIN,
                            Phone = @Phone,
                            Email = @Email
                            WHERE Code = @Code";

                var parameters = new Dictionary<string, object>
                {
                    { "@Code", agency.Code },
                    { "@AgencyName", agency.AgencyName },
                    { "@Address", agency.Address ?? (object)DBNull.Value },
                    { "@PIN", agency.PIN ?? (object)DBNull.Value },
                    { "@Phone", agency.Phone ?? (object)DBNull.Value },
                    { "@Email", agency.Email ?? (object)DBNull.Value }
                };

                var result = await router.WriteFanOutAsync(_channelDatabaseName, sql, parameters, cancellationToken);

                if (result.AnySucceeded)
                {
                    _lastRefresh = DateTime.MinValue;
                    LogService.Info($"Updated agency '{agency.AgencyName}' (Code: {agency.Code}) in {_channelDatabaseName}");
                }

                return result.AnySucceeded
                    ? DbOperationResult.Succeeded(_channelDatabaseName, result.SuccessCount)
                    : DbOperationResult.Failed(_channelDatabaseName, result.GetSummary());
            }
            catch (Exception ex)
            {
                LogService.Error("Exception updating agency", ex);
                return DbOperationResult.Failed(_channelDatabaseName, ex.Message, ex);
            }
        }

        /// <summary>
        /// Check if an agency is referenced by any commercials
        /// </summary>
        public async Task<bool> IsAgencyReferencedAsync(int agencyCode, CancellationToken cancellationToken = default)
        {
            try
            {
                var router = DatabaseRouter.Instance;
                // Check if referenced in commercials table (Code column stores agency code)
                var sql = $"SELECT COUNT(*) as cnt FROM commercials WHERE Code = @Code";

                var result = await router.ReadFirstSuccessAsync(
                    _channelDatabaseName,
                    sql,
                    reader =>
                    {
                        // Reader is already positioned on first row
                        return reader.GetValueOrDefault<int>("cnt") > 0;
                    },
                    new Dictionary<string, object> { { "@Code", agencyCode } },
                    cancellationToken);

                return result.Success && result.Data;
            }
            catch (Exception ex)
            {
                LogService.Error("Exception checking agency references", ex);
                return true; // Assume referenced for safety
            }
        }

        /// <summary>
        /// Delete an agency (fan-out write to all servers)
        /// </summary>
        public async Task<DbOperationResult> DeleteAgencyAsync(int agencyCode, CancellationToken cancellationToken = default)
        {
            if (agencyCode <= 0)
            {
                return DbOperationResult.Failed(_channelDatabaseName, "Invalid agency code");
            }

            // Check if referenced
            if (await IsAgencyReferencedAsync(agencyCode, cancellationToken))
            {
                return DbOperationResult.Failed(_channelDatabaseName,
                    "Cannot delete agency: it is referenced by one or more commercials");
            }

            try
            {
                var router = DatabaseRouter.Instance;
                var sql = $"DELETE FROM {TABLE_NAME} WHERE Code = @Code";
                var parameters = new Dictionary<string, object> { { "@Code", agencyCode } };

                var result = await router.WriteFanOutAsync(_channelDatabaseName, sql, parameters, cancellationToken);

                if (result.AnySucceeded)
                {
                    _lastRefresh = DateTime.MinValue;
                    LogService.Info($"Deleted agency (Code: {agencyCode}) from {_channelDatabaseName}");
                }

                return result.AnySucceeded
                    ? DbOperationResult.Succeeded(_channelDatabaseName, result.SuccessCount)
                    : DbOperationResult.Failed(_channelDatabaseName, result.GetSummary());
            }
            catch (Exception ex)
            {
                LogService.Error("Exception deleting agency", ex);
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
