using System;

namespace AirStudio.CommercialManager.Core.Models
{
    /// <summary>
    /// Represents a MySQL database connection profile
    /// </summary>
    public class DatabaseProfile
    {
        /// <summary>
        /// Unique identifier for this profile
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Display name for this profile (e.g., "Primary Server", "Backup Server")
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// MySQL server host or IP address
        /// </summary>
        public string Host { get; set; } = "localhost";

        /// <summary>
        /// MySQL server port
        /// </summary>
        public int Port { get; set; } = 3306;

        /// <summary>
        /// MySQL username
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// MySQL password (stored encrypted)
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Display-friendly password (shows asterisks for security)
        /// </summary>
        public string PasswordDisplay => string.IsNullOrEmpty(Password) ? "" : "********";

        /// <summary>
        /// SSL mode for the connection (default: None for local networks)
        /// </summary>
        public string SslMode { get; set; } = "None";

        /// <summary>
        /// Connection timeout in seconds
        /// </summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Whether this is the default profile for normal operations
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// Order in the fallback sequence (lower = higher priority)
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Build a MySqlConnector connection string (password must be decrypted first)
        /// </summary>
        public string BuildConnectionString(string decryptedPassword, string database = null)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append($"Server={Host};");
            builder.Append($"Port={Port};");
            builder.Append($"User ID={Username};");
            builder.Append($"Password={decryptedPassword};");
            builder.Append($"SslMode={SslMode};");
            builder.Append($"Connection Timeout={TimeoutSeconds};");

            if (!string.IsNullOrEmpty(database))
            {
                builder.Append($"Database={database};");
            }

            return builder.ToString();
        }

        public override string ToString()
        {
            return $"{Name} ({Host}:{Port})";
        }
    }
}
