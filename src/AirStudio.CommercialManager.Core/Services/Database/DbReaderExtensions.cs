using System;
using MySqlConnector;

namespace AirStudio.CommercialManager.Core.Services.Database
{
    /// <summary>
    /// Extension methods for MySqlDataReader to safely read values
    /// </summary>
    public static class DbReaderExtensions
    {
        /// <summary>
        /// Get a string value or null if DBNull
        /// </summary>
        public static string GetStringOrNull(this MySqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        /// <summary>
        /// Get a string value or empty string if DBNull
        /// </summary>
        public static string GetStringOrEmpty(this MySqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        /// <summary>
        /// Get an int value or 0 if DBNull
        /// </summary>
        public static int GetInt32OrDefault(this MySqlDataReader reader, string columnName, int defaultValue = 0)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetInt32(ordinal);
        }

        /// <summary>
        /// Get a long value or 0 if DBNull
        /// </summary>
        public static long GetInt64OrDefault(this MySqlDataReader reader, string columnName, long defaultValue = 0)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetInt64(ordinal);
        }

        /// <summary>
        /// Get a double value or 0 if DBNull
        /// </summary>
        public static double GetDoubleOrDefault(this MySqlDataReader reader, string columnName, double defaultValue = 0)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetDouble(ordinal);
        }

        /// <summary>
        /// Get a decimal value or 0 if DBNull
        /// </summary>
        public static decimal GetDecimalOrDefault(this MySqlDataReader reader, string columnName, decimal defaultValue = 0)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetDecimal(ordinal);
        }

        /// <summary>
        /// Get a DateTime value or DateTime.MinValue if DBNull
        /// </summary>
        public static DateTime GetDateTimeOrDefault(this MySqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? DateTime.MinValue : reader.GetDateTime(ordinal);
        }

        /// <summary>
        /// Get a nullable DateTime value
        /// </summary>
        public static DateTime? GetDateTimeOrNull(this MySqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? (DateTime?)null : reader.GetDateTime(ordinal);
        }

        /// <summary>
        /// Get a bool value or false if DBNull
        /// </summary>
        public static bool GetBooleanOrDefault(this MySqlDataReader reader, string columnName, bool defaultValue = false)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetBoolean(ordinal);
        }

        /// <summary>
        /// Get a TimeSpan from a string column (HH:mm:ss format)
        /// </summary>
        public static TimeSpan GetTimeSpanFromString(this MySqlDataReader reader, string columnName)
        {
            var value = reader.GetStringOrEmpty(columnName);
            if (TimeSpan.TryParse(value, out var result))
            {
                return result;
            }
            return TimeSpan.Zero;
        }

        /// <summary>
        /// Check if a column exists in the reader
        /// </summary>
        public static bool HasColumn(this MySqlDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Safely get a value by column name, returning default if not found or DBNull
        /// </summary>
        public static T GetValueOrDefault<T>(this MySqlDataReader reader, string columnName, T defaultValue = default)
        {
            try
            {
                var ordinal = reader.GetOrdinal(columnName);
                if (reader.IsDBNull(ordinal))
                {
                    return defaultValue;
                }

                var value = reader.GetValue(ordinal);
                if (value is T typedValue)
                {
                    return typedValue;
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
    }
}
