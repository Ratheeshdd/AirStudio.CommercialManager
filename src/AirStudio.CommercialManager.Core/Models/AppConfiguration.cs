using System;
using System.Collections.Generic;
using System.Linq;

namespace AirStudio.CommercialManager.Core.Models
{
    /// <summary>
    /// Application configuration stored in ProgramData
    /// </summary>
    public class AppConfiguration
    {
        /// <summary>
        /// Configuration file version for migration support
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// Ordered list of database profiles (first is default, rest are fallbacks)
        /// </summary>
        public List<DatabaseProfile> DatabaseProfiles { get; set; } = new List<DatabaseProfile>();

        /// <summary>
        /// Channel to X root target mappings
        /// </summary>
        public List<ChannelLocation> ChannelLocations { get; set; } = new List<ChannelLocation>();

        /// <summary>
        /// List of allowed users (DOMAIN\user or MACHINE\user format)
        /// </summary>
        public List<string> AllowedUsers { get; set; } = new List<string>();

        /// <summary>
        /// Base NN value for TAG sequence tokens (default 27)
        /// </summary>
        public int SequenceBaseNN { get; set; } = 27;

        /// <summary>
        /// Last modified timestamp
        /// </summary>
        public DateTime LastModified { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Get the default database profile
        /// </summary>
        public DatabaseProfile DefaultProfile =>
            DatabaseProfiles?.FirstOrDefault(p => p.IsDefault) ??
            DatabaseProfiles?.OrderBy(p => p.Order).FirstOrDefault();

        /// <summary>
        /// Get database profiles ordered by priority
        /// </summary>
        public IEnumerable<DatabaseProfile> OrderedProfiles =>
            DatabaseProfiles?.OrderBy(p => p.Order) ?? Enumerable.Empty<DatabaseProfile>();

        /// <summary>
        /// Get channel location by channel name
        /// </summary>
        public ChannelLocation GetChannelLocation(string channel)
        {
            return ChannelLocations?.FirstOrDefault(c =>
                string.Equals(c.Channel, channel, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Check if a user is in the allowed list
        /// </summary>
        public bool IsUserAllowed(string username)
        {
            if (AllowedUsers == null || AllowedUsers.Count == 0)
            {
                // If no users configured, allow all (for initial setup)
                return true;
            }

            return AllowedUsers.Any(u =>
                string.Equals(u, username, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Check if configuration is valid for operation
        /// </summary>
        public bool IsValid =>
            DatabaseProfiles != null &&
            DatabaseProfiles.Count > 0 &&
            DatabaseProfiles.Any(p => p.IsDefault);

        /// <summary>
        /// Create a default configuration for first-time setup
        /// </summary>
        public static AppConfiguration CreateDefault()
        {
            return new AppConfiguration
            {
                Version = 1,
                DatabaseProfiles = new List<DatabaseProfile>
                {
                    new DatabaseProfile
                    {
                        Name = "Default Server",
                        Host = "localhost",
                        Port = 3306,
                        Username = "root",
                        Password = string.Empty,
                        IsDefault = true,
                        Order = 0
                    }
                },
                ChannelLocations = new List<ChannelLocation>(),
                AllowedUsers = new List<string>(),
                SequenceBaseNN = 27,
                LastModified = DateTime.UtcNow
            };
        }
    }
}
