using System;
using System.IO;
using Serilog;
using Serilog.Events;

namespace AirStudio.CommercialManager.Core.Services.Logging
{
    /// <summary>
    /// Centralized logging service using Serilog
    /// </summary>
    public static class LogService
    {
        private static ILogger _logger;
        private static readonly object _lock = new object();
        private static bool _initialized = false;

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
        /// Log a debug message
        /// </summary>
        public static void Debug(string message)
        {
            EnsureInitialized();
            _logger.Debug(message);
        }

        /// <summary>
        /// Log an info message
        /// </summary>
        public static void Info(string message)
        {
            EnsureInitialized();
            _logger.Information(message);
        }

        /// <summary>
        /// Log a warning message
        /// </summary>
        public static void Warning(string message)
        {
            EnsureInitialized();
            _logger.Warning(message);
        }

        /// <summary>
        /// Log a warning message with exception
        /// </summary>
        public static void Warning(string message, Exception ex)
        {
            EnsureInitialized();
            _logger.Warning(ex, message);
        }

        /// <summary>
        /// Log an error message
        /// </summary>
        public static void Error(string message)
        {
            EnsureInitialized();
            _logger.Error(message);
        }

        /// <summary>
        /// Log an error message with exception
        /// </summary>
        public static void Error(string message, Exception ex)
        {
            EnsureInitialized();
            _logger.Error(ex, message);
        }

        /// <summary>
        /// Log a fatal error message
        /// </summary>
        public static void Fatal(string message, Exception ex)
        {
            EnsureInitialized();
            _logger.Fatal(ex, message);
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
