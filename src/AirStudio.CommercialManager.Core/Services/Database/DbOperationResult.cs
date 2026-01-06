using System;
using System.Collections.Generic;

namespace AirStudio.CommercialManager.Core.Services.Database
{
    /// <summary>
    /// Result of a database operation on a single server
    /// </summary>
    public class DbOperationResult
    {
        /// <summary>
        /// Profile name that was used
        /// </summary>
        public string ProfileName { get; set; }

        /// <summary>
        /// Whether the operation succeeded
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if failed
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Exception if failed
        /// </summary>
        public Exception Exception { get; set; }

        /// <summary>
        /// Number of rows affected (for write operations)
        /// </summary>
        public int RowsAffected { get; set; }

        /// <summary>
        /// Last inserted ID (for insert operations)
        /// </summary>
        public long LastInsertId { get; set; }

        /// <summary>
        /// Time taken for the operation
        /// </summary>
        public TimeSpan Duration { get; set; }

        public static DbOperationResult Succeeded(string profileName, int rowsAffected = 0, long lastInsertId = 0)
        {
            return new DbOperationResult
            {
                ProfileName = profileName,
                Success = true,
                RowsAffected = rowsAffected,
                LastInsertId = lastInsertId
            };
        }

        public static DbOperationResult Failed(string profileName, string message, Exception ex = null)
        {
            return new DbOperationResult
            {
                ProfileName = profileName,
                Success = false,
                ErrorMessage = message,
                Exception = ex
            };
        }
    }

    /// <summary>
    /// Result of a read operation (returns data)
    /// </summary>
    public class DbReadResult<T> : DbOperationResult
    {
        /// <summary>
        /// The data returned from the query
        /// </summary>
        public T Data { get; set; }

        /// <summary>
        /// List of items (for queries returning multiple rows)
        /// </summary>
        public List<T> Items { get; set; }

        public static DbReadResult<T> SucceededWithData(string profileName, T data)
        {
            return new DbReadResult<T>
            {
                ProfileName = profileName,
                Success = true,
                Data = data
            };
        }

        public static DbReadResult<T> SucceededWithItems(string profileName, List<T> items)
        {
            return new DbReadResult<T>
            {
                ProfileName = profileName,
                Success = true,
                Items = items
            };
        }

        public new static DbReadResult<T> Failed(string profileName, string message, Exception ex = null)
        {
            return new DbReadResult<T>
            {
                ProfileName = profileName,
                Success = false,
                ErrorMessage = message,
                Exception = ex
            };
        }
    }

    /// <summary>
    /// Aggregate result from fan-out write operations
    /// </summary>
    public class FanOutWriteResult
    {
        /// <summary>
        /// Individual results from each server
        /// </summary>
        public List<DbOperationResult> Results { get; set; } = new List<DbOperationResult>();

        /// <summary>
        /// Number of servers that succeeded
        /// </summary>
        public int SuccessCount => Results?.FindAll(r => r.Success).Count ?? 0;

        /// <summary>
        /// Number of servers that failed
        /// </summary>
        public int FailureCount => Results?.FindAll(r => !r.Success).Count ?? 0;

        /// <summary>
        /// Total number of servers attempted
        /// </summary>
        public int TotalCount => Results?.Count ?? 0;

        /// <summary>
        /// Whether all servers succeeded
        /// </summary>
        public bool AllSucceeded => FailureCount == 0 && TotalCount > 0;

        /// <summary>
        /// Whether at least one server succeeded
        /// </summary>
        public bool AnySucceeded => SuccessCount > 0;

        /// <summary>
        /// Whether all servers failed
        /// </summary>
        public bool AllFailed => SuccessCount == 0;

        /// <summary>
        /// Get failed results
        /// </summary>
        public List<DbOperationResult> FailedResults => Results?.FindAll(r => !r.Success) ?? new List<DbOperationResult>();

        /// <summary>
        /// Get a summary message
        /// </summary>
        public string GetSummary()
        {
            if (AllSucceeded)
                return $"Success: All {TotalCount} servers updated";
            if (AllFailed)
                return $"Failed: All {TotalCount} servers failed";
            return $"Partial: {SuccessCount}/{TotalCount} servers succeeded";
        }
    }
}
