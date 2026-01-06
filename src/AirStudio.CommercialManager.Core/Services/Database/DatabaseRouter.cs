using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Configuration;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Core.Services.Database
{
    /// <summary>
    /// Multi-database router implementing:
    /// - Parallel-first-success reads (query multiple servers, return first success)
    /// - Fan-out writes (write to ALL servers, warn on partial failure)
    /// - Self-healing updates (UPDATE first, INSERT if 0 rows affected)
    /// </summary>
    public class DatabaseRouter
    {
        private static readonly object _lock = new object();
        private static DatabaseRouter _instance;

        // Maximum concurrent database queries for reads
        private const int MAX_READ_CONCURRENCY = 3;

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static DatabaseRouter Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new DatabaseRouter();
                        }
                    }
                }
                return _instance;
            }
        }

        private DatabaseRouter() { }

        #region Connection Helpers

        /// <summary>
        /// Get ordered list of database profiles from configuration
        /// </summary>
        private List<DatabaseProfile> GetOrderedProfiles()
        {
            var config = ConfigurationService.Instance.CurrentConfig;
            if (config?.DatabaseProfiles == null || config.DatabaseProfiles.Count == 0)
            {
                LogService.Warning("No database profiles configured");
                return new List<DatabaseProfile>();
            }

            return config.OrderedProfiles.ToList();
        }

        /// <summary>
        /// Create a connection to a specific profile
        /// </summary>
        private MySqlConnection CreateConnection(DatabaseProfile profile, string database = null)
        {
            var password = ConfigurationService.Instance.GetDecryptedPassword(profile);
            var connectionString = profile.BuildConnectionString(password, database);
            return new MySqlConnection(connectionString);
        }

        /// <summary>
        /// Test connection to a profile
        /// </summary>
        public async Task<DbOperationResult> TestConnectionAsync(DatabaseProfile profile, string database = null)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using (var connection = CreateConnection(profile, database))
                {
                    await connection.OpenAsync();
                    sw.Stop();

                    return new DbOperationResult
                    {
                        ProfileName = profile.Name,
                        Success = true,
                        Duration = sw.Elapsed
                    };
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                return DbOperationResult.Failed(profile.Name, ex.Message, ex);
            }
        }

        #endregion

        #region Parallel-First-Success Reads

        /// <summary>
        /// Execute a read query using parallel-first-success strategy.
        /// Queries multiple servers in parallel, returns first successful result.
        /// </summary>
        public async Task<DbReadResult<T>> ReadFirstSuccessAsync<T>(
            string database,
            string sql,
            Func<MySqlDataReader, T> mapper,
            object parameters = null,
            CancellationToken cancellationToken = default)
        {
            var profiles = GetOrderedProfiles();
            if (profiles.Count == 0)
            {
                return DbReadResult<T>.Failed("None", "No database profiles configured");
            }

            // Take up to MAX_READ_CONCURRENCY profiles
            var profilesToQuery = profiles.Take(MAX_READ_CONCURRENCY).ToList();

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var tasks = profilesToQuery.Select(profile =>
                    ExecuteReadAsync(profile, database, sql, mapper, parameters, cts.Token)).ToList();

                // Wait for first successful completion
                while (tasks.Count > 0)
                {
                    var completedTask = await Task.WhenAny(tasks);
                    tasks.Remove(completedTask);

                    try
                    {
                        var result = await completedTask;
                        if (result.Success)
                        {
                            // Cancel remaining tasks
                            cts.Cancel();
                            LogService.Debug($"Read succeeded from {result.ProfileName}");
                            return result;
                        }
                        else
                        {
                            LogService.Warning($"Read failed from {result.ProfileName}: {result.ErrorMessage}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Task was cancelled, ignore
                    }
                    catch (Exception ex)
                    {
                        LogService.Warning($"Read task exception: {ex.Message}");
                    }
                }
            }

            // All failed
            LogService.Error("All database read attempts failed");
            return DbReadResult<T>.Failed("All", "All database servers failed");
        }

        /// <summary>
        /// Execute a read query returning a list of items using parallel-first-success strategy.
        /// </summary>
        public async Task<DbReadResult<T>> ReadListFirstSuccessAsync<T>(
            string database,
            string sql,
            Func<MySqlDataReader, T> mapper,
            object parameters = null,
            CancellationToken cancellationToken = default)
        {
            var profiles = GetOrderedProfiles();
            if (profiles.Count == 0)
            {
                return DbReadResult<T>.Failed("None", "No database profiles configured");
            }

            var profilesToQuery = profiles.Take(MAX_READ_CONCURRENCY).ToList();

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var tasks = profilesToQuery.Select(profile =>
                    ExecuteReadListAsync(profile, database, sql, mapper, parameters, cts.Token)).ToList();

                while (tasks.Count > 0)
                {
                    var completedTask = await Task.WhenAny(tasks);
                    tasks.Remove(completedTask);

                    try
                    {
                        var result = await completedTask;
                        if (result.Success)
                        {
                            cts.Cancel();
                            LogService.Debug($"Read list succeeded from {result.ProfileName} ({result.Items?.Count ?? 0} items)");
                            return result;
                        }
                        else
                        {
                            LogService.Warning($"Read list failed from {result.ProfileName}: {result.ErrorMessage}");
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        LogService.Warning($"Read list task exception: {ex.Message}");
                    }
                }
            }

            LogService.Error("All database read list attempts failed");
            return DbReadResult<T>.Failed("All", "All database servers failed");
        }

        /// <summary>
        /// Execute a scalar read using parallel-first-success strategy.
        /// </summary>
        public async Task<DbReadResult<T>> ReadScalarFirstSuccessAsync<T>(
            string database,
            string sql,
            object parameters = null,
            CancellationToken cancellationToken = default)
        {
            var profiles = GetOrderedProfiles();
            if (profiles.Count == 0)
            {
                return DbReadResult<T>.Failed("None", "No database profiles configured");
            }

            var profilesToQuery = profiles.Take(MAX_READ_CONCURRENCY).ToList();

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var tasks = profilesToQuery.Select(profile =>
                    ExecuteScalarAsync<T>(profile, database, sql, parameters, cts.Token)).ToList();

                while (tasks.Count > 0)
                {
                    var completedTask = await Task.WhenAny(tasks);
                    tasks.Remove(completedTask);

                    try
                    {
                        var result = await completedTask;
                        if (result.Success)
                        {
                            cts.Cancel();
                            LogService.Debug($"Scalar read succeeded from {result.ProfileName}");
                            return result;
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        LogService.Warning($"Scalar read task exception: {ex.Message}");
                    }
                }
            }

            return DbReadResult<T>.Failed("All", "All database servers failed");
        }

        #endregion

        #region Fan-Out Writes

        /// <summary>
        /// Execute a write operation on ALL configured database servers (fan-out).
        /// Returns aggregate result with per-server status.
        /// </summary>
        public async Task<FanOutWriteResult> WriteFanOutAsync(
            string database,
            string sql,
            object parameters = null,
            CancellationToken cancellationToken = default)
        {
            var profiles = GetOrderedProfiles();
            var result = new FanOutWriteResult();

            if (profiles.Count == 0)
            {
                result.Results.Add(DbOperationResult.Failed("None", "No database profiles configured"));
                return result;
            }

            // Execute on all servers in parallel
            var tasks = profiles.Select(profile =>
                ExecuteWriteAsync(profile, database, sql, parameters, cancellationToken)).ToList();

            var results = await Task.WhenAll(tasks);
            result.Results.AddRange(results);

            // Log summary
            if (result.AllSucceeded)
            {
                LogService.Info($"Fan-out write succeeded on all {result.TotalCount} servers");
            }
            else if (result.AnySucceeded)
            {
                LogService.Warning($"Fan-out write partial: {result.SuccessCount}/{result.TotalCount} succeeded");
                foreach (var failed in result.FailedResults)
                {
                    LogService.Warning($"  Failed on {failed.ProfileName}: {failed.ErrorMessage}");
                }
            }
            else
            {
                LogService.Error($"Fan-out write failed on all {result.TotalCount} servers");
            }

            return result;
        }

        /// <summary>
        /// Execute a self-healing update/insert on ALL servers.
        /// Tries UPDATE first; if 0 rows affected, executes INSERT.
        /// </summary>
        public async Task<FanOutWriteResult> WriteSelfHealingAsync(
            string database,
            string updateSql,
            string insertSql,
            object updateParameters = null,
            object insertParameters = null,
            CancellationToken cancellationToken = default)
        {
            var profiles = GetOrderedProfiles();
            var result = new FanOutWriteResult();

            if (profiles.Count == 0)
            {
                result.Results.Add(DbOperationResult.Failed("None", "No database profiles configured"));
                return result;
            }

            var tasks = profiles.Select(profile =>
                ExecuteSelfHealingAsync(profile, database, updateSql, insertSql,
                    updateParameters, insertParameters, cancellationToken)).ToList();

            var results = await Task.WhenAll(tasks);
            result.Results.AddRange(results);

            if (result.AllSucceeded)
            {
                LogService.Info($"Self-healing write succeeded on all {result.TotalCount} servers");
            }
            else if (result.AnySucceeded)
            {
                LogService.Warning($"Self-healing write partial: {result.SuccessCount}/{result.TotalCount} succeeded");
            }
            else
            {
                LogService.Error($"Self-healing write failed on all {result.TotalCount} servers");
            }

            return result;
        }

        #endregion

        #region Private Execution Methods

        private async Task<DbReadResult<T>> ExecuteReadAsync<T>(
            DatabaseProfile profile,
            string database,
            string sql,
            Func<MySqlDataReader, T> mapper,
            object parameters,
            CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using (var connection = CreateConnection(profile, database))
                {
                    await connection.OpenAsync(cancellationToken);

                    using (var command = new MySqlCommand(sql, connection))
                    {
                        AddParameters(command, parameters);
                        command.CommandTimeout = profile.TimeoutSeconds;

                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            if (await reader.ReadAsync(cancellationToken))
                            {
                                var data = mapper(reader);
                                sw.Stop();
                                return new DbReadResult<T>
                                {
                                    ProfileName = profile.Name,
                                    Success = true,
                                    Data = data,
                                    Duration = sw.Elapsed
                                };
                            }
                            else
                            {
                                sw.Stop();
                                return new DbReadResult<T>
                                {
                                    ProfileName = profile.Name,
                                    Success = true,
                                    Data = default,
                                    Duration = sw.Elapsed
                                };
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                return DbReadResult<T>.Failed(profile.Name, ex.Message, ex);
            }
        }

        private async Task<DbReadResult<T>> ExecuteReadListAsync<T>(
            DatabaseProfile profile,
            string database,
            string sql,
            Func<MySqlDataReader, T> mapper,
            object parameters,
            CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using (var connection = CreateConnection(profile, database))
                {
                    await connection.OpenAsync(cancellationToken);

                    using (var command = new MySqlCommand(sql, connection))
                    {
                        AddParameters(command, parameters);
                        command.CommandTimeout = profile.TimeoutSeconds;

                        var items = new List<T>();
                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                items.Add(mapper(reader));
                            }
                        }

                        sw.Stop();
                        return new DbReadResult<T>
                        {
                            ProfileName = profile.Name,
                            Success = true,
                            Items = items,
                            Duration = sw.Elapsed
                        };
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                return DbReadResult<T>.Failed(profile.Name, ex.Message, ex);
            }
        }

        private async Task<DbReadResult<T>> ExecuteScalarAsync<T>(
            DatabaseProfile profile,
            string database,
            string sql,
            object parameters,
            CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using (var connection = CreateConnection(profile, database))
                {
                    await connection.OpenAsync(cancellationToken);

                    using (var command = new MySqlCommand(sql, connection))
                    {
                        AddParameters(command, parameters);
                        command.CommandTimeout = profile.TimeoutSeconds;

                        var result = await command.ExecuteScalarAsync(cancellationToken);
                        sw.Stop();

                        T data = default;
                        if (result != null && result != DBNull.Value)
                        {
                            data = (T)Convert.ChangeType(result, typeof(T));
                        }

                        return new DbReadResult<T>
                        {
                            ProfileName = profile.Name,
                            Success = true,
                            Data = data,
                            Duration = sw.Elapsed
                        };
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                return DbReadResult<T>.Failed(profile.Name, ex.Message, ex);
            }
        }

        private async Task<DbOperationResult> ExecuteWriteAsync(
            DatabaseProfile profile,
            string database,
            string sql,
            object parameters,
            CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using (var connection = CreateConnection(profile, database))
                {
                    await connection.OpenAsync(cancellationToken);

                    using (var command = new MySqlCommand(sql, connection))
                    {
                        AddParameters(command, parameters);
                        command.CommandTimeout = profile.TimeoutSeconds;

                        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
                        sw.Stop();

                        return new DbOperationResult
                        {
                            ProfileName = profile.Name,
                            Success = true,
                            RowsAffected = rowsAffected,
                            LastInsertId = command.LastInsertedId,
                            Duration = sw.Elapsed
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                return DbOperationResult.Failed(profile.Name, ex.Message, ex);
            }
        }

        private async Task<DbOperationResult> ExecuteSelfHealingAsync(
            DatabaseProfile profile,
            string database,
            string updateSql,
            string insertSql,
            object updateParameters,
            object insertParameters,
            CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using (var connection = CreateConnection(profile, database))
                {
                    await connection.OpenAsync(cancellationToken);

                    // Try UPDATE first
                    using (var updateCommand = new MySqlCommand(updateSql, connection))
                    {
                        AddParameters(updateCommand, updateParameters);
                        updateCommand.CommandTimeout = profile.TimeoutSeconds;

                        var rowsAffected = await updateCommand.ExecuteNonQueryAsync(cancellationToken);

                        if (rowsAffected > 0)
                        {
                            sw.Stop();
                            LogService.Debug($"Self-healing UPDATE succeeded on {profile.Name}: {rowsAffected} rows");
                            return new DbOperationResult
                            {
                                ProfileName = profile.Name,
                                Success = true,
                                RowsAffected = rowsAffected,
                                Duration = sw.Elapsed
                            };
                        }
                    }

                    // UPDATE affected 0 rows, try INSERT
                    using (var insertCommand = new MySqlCommand(insertSql, connection))
                    {
                        AddParameters(insertCommand, insertParameters);
                        insertCommand.CommandTimeout = profile.TimeoutSeconds;

                        var rowsAffected = await insertCommand.ExecuteNonQueryAsync(cancellationToken);
                        sw.Stop();

                        LogService.Debug($"Self-healing INSERT on {profile.Name}: {rowsAffected} rows, ID={insertCommand.LastInsertedId}");
                        return new DbOperationResult
                        {
                            ProfileName = profile.Name,
                            Success = true,
                            RowsAffected = rowsAffected,
                            LastInsertId = insertCommand.LastInsertedId,
                            Duration = sw.Elapsed
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                return DbOperationResult.Failed(profile.Name, ex.Message, ex);
            }
        }

        private void AddParameters(MySqlCommand command, object parameters)
        {
            if (parameters == null) return;

            // Support anonymous objects and dictionaries
            if (parameters is IDictionary<string, object> dict)
            {
                foreach (var kvp in dict)
                {
                    command.Parameters.AddWithValue("@" + kvp.Key, kvp.Value ?? DBNull.Value);
                }
            }
            else
            {
                // Use reflection for anonymous objects
                var properties = parameters.GetType().GetProperties();
                foreach (var prop in properties)
                {
                    var value = prop.GetValue(parameters);
                    command.Parameters.AddWithValue("@" + prop.Name, value ?? DBNull.Value);
                }
            }
        }

        #endregion
    }
}
