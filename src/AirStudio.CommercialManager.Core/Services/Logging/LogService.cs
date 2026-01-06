using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;
using Serilog.Events;

namespace AirStudio.CommercialManager.Core.Services.Logging
{
    /// <summary>
    /// Represents a log entry for UI display
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public string Exception { get; set; }

        public bool IsError => Level == "ERR" || Level == "FTL";
        public bool IsWarning => Level == "WRN";

        public override string ToString() =>
            $"{Timestamp:HH:mm:ss} [{Level}] {Message}";
    }

    /// <summary>
    /// Centralized logging service using Serilog
    /// </summary>
    public static class LogService
    {
        private static ILogger _logger;
        private static readonly object _lock = new object();
        private static bool _initialized = false;

        private static readonly List<LogEntry> _recentLogs = new List<LogEntry>();
        private const int MAX_RECENT_LOGS = 500;

        /// <summary>
        /// Event raised when a new log entry is added
        /// </summary>
        public static event EventHandler<LogEntry> LogAdded;

        /// <summary>
        /// Initialize the logging service
        /// </summary>
        public static void Initialize()
        {
            lock (_lock)
            {
                if (_initialized) return;

                var logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "AirStudio",
                    "CommercialManager",
                    "Logs");

                Directory.CreateDirectory(logDirectory);

                var logPath = Path.Combine(logDirectory, "CommercialManager-.log");

                _logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.File(
                        logPath,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();

                _initialized = true;
                Info("Logging initialized. Log directory: " + logDirectory);
            }
        }

        /// <summary>
        /// Shutdown the logging service
        /// </summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                if (_logger is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                _initialized = false;
            }
        }

        /// <summary>
        /// Get recent log entries
        /// </summary>
        public static List<LogEntry> GetRecentLogs(int count = 100)
        {
            lock (_recentLogs)
            {
                return _recentLogs.TakeLast(count).ToList();
            }
        }

        /// <summary>
        /// Get error logs only
        /// </summary>
        public static List<LogEntry> GetErrorLogs(int count = 50)
        {
            lock (_recentLogs)
            {
                return _recentLogs.Where(l => l.IsError).TakeLast(count).ToList();
            }
        }

        /// <summary>
        /// Clear recent logs
        /// </summary>
        public static void ClearRecentLogs()
        {
            lock (_recentLogs)
            {
                _recentLogs.Clear();
            }
        }

        private static void AddLogEntry(string level, string message, Exception ex = null)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Exception = ex?.ToString()
            };

            lock (_recentLogs)
            {
                _recentLogs.Add(entry);
                while (_recentLogs.Count > MAX_RECENT_LOGS)
                {
                    _recentLogs.RemoveAt(0);
                }
            }

            LogAdded?.Invoke(null, entry);
        }

        /// <summary>
        /// Log a debug message
        /// </summary>
        public static void Debug(string message)
        {
            EnsureInitialized();
            _logger.Debug(message);
            AddLogEntry("DBG", message);
        }

        /// <summary>
        /// Log an info message
        /// </summary>
        public static void Info(string message)
        {
            EnsureInitialized();
            _logger.Information(message);
            AddLogEntry("INF", message);
        }

        /// <summary>
        /// Log a warning message
        /// </summary>
        public static void Warning(string message)
        {
            EnsureInitialized();
            _logger.Warning(message);
            AddLogEntry("WRN", message);
        }

        /// <summary>
        /// Log a warning message with exception
        /// </summary>
        public static void Warning(string message, Exception ex)
        {
            EnsureInitialized();
            _logger.Warning(ex, message);
            AddLogEntry("WRN", message, ex);
        }

        /// <summary>
        /// Log an error message
        /// </summary>
        public static void Error(string message)
        {
            EnsureInitialized();
            _logger.Error(message);
            AddLogEntry("ERR", message);
        }

        /// <summary>
        /// Log an error message with exception
        /// </summary>
        public static void Error(string message, Exception ex)
        {
            EnsureInitialized();
            _logger.Error(ex, message);
            AddLogEntry("ERR", message, ex);
        }

        /// <summary>
        /// Log a fatal error message
        /// </summary>
        public static void Fatal(string message, Exception ex)
        {
            EnsureInitialized();
            _logger.Fatal(ex, message);
            AddLogEntry("FTL", message, ex);
        }

        private static void EnsureInitialized()
        {
            if (!_initialized)
            {
                Initialize();
            }
        }
    }
}
