using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AirStudio.CommercialManager.Core.Models
{
    /// <summary>
    /// Represents a broadcast channel
    /// </summary>
    public class Channel
    {
        /// <summary>
        /// Channel name (from setup_channels table)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// X root targets configured for this channel (e.g., X:\, Y:\)
        /// </summary>
        public List<string> XRootTargets { get; set; } = new List<string>();

        /// <summary>
        /// Whether this channel was loaded from config (not in DB)
        /// </summary>
        public bool IsFromConfig { get; set; }

        /// <summary>
        /// Channel is usable only if it has at least one X root target configured
        /// </summary>
        public bool IsUsable => XRootTargets != null && XRootTargets.Count > 0;

        /// <summary>
        /// Get the database name for this channel (air_<channel>)
        /// </summary>
        public string DatabaseName => $"air_{Name}";

        /// <summary>
        /// Get the commercial playlist folder path for a specific X target
        /// </summary>
        public string GetPlaylistPath(string xRoot)
        {
            return Path.Combine(xRoot, "Commercial Playlist");
        }

        /// <summary>
        /// Get the commercials audio folder path for a specific X target
        /// </summary>
        public string GetCommercialsPath(string xRoot)
        {
            return Path.Combine(xRoot, "Commercials");
        }

        /// <summary>
        /// Get all playlist paths for all configured X targets
        /// </summary>
        public List<string> GetAllPlaylistPaths()
        {
            return XRootTargets?.Select(x => GetPlaylistPath(x)).ToList() ?? new List<string>();
        }

        /// <summary>
        /// Get all commercial audio paths for all configured X targets
        /// </summary>
        public List<string> GetAllCommercialsPaths()
        {
            return XRootTargets?.Select(x => GetCommercialsPath(x)).ToList() ?? new List<string>();
        }

        /// <summary>
        /// Get the primary X root target (first in list)
        /// </summary>
        public string PrimaryXRoot => XRootTargets?.FirstOrDefault();

        /// <summary>
        /// Get the primary playlist path
        /// </summary>
        public string PrimaryPlaylistPath => PrimaryXRoot != null ? GetPlaylistPath(PrimaryXRoot) : null;

        /// <summary>
        /// Get the primary commercials audio path
        /// </summary>
        public string PrimaryCommercialsPath => PrimaryXRoot != null ? GetCommercialsPath(PrimaryXRoot) : null;

        /// <summary>
        /// Display string for UI
        /// </summary>
        public string DisplayName => IsUsable ? Name : $"{Name} (not configured)";

        /// <summary>
        /// Check if a specific X target is accessible (exists and writable)
        /// </summary>
        public bool IsTargetAccessible(string xRoot)
        {
            try
            {
                if (!Directory.Exists(xRoot)) return false;

                // Try to write a test file
                var testFile = Path.Combine(xRoot, ".accesstest");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get accessible X targets
        /// </summary>
        public List<string> GetAccessibleTargets()
        {
            return XRootTargets?.Where(IsTargetAccessible).ToList() ?? new List<string>();
        }

        /// <summary>
        /// Ensure required directories exist on a target
        /// </summary>
        public void EnsureDirectoriesExist(string xRoot)
        {
            var playlistPath = GetPlaylistPath(xRoot);
            var commercialsPath = GetCommercialsPath(xRoot);

            if (!Directory.Exists(playlistPath))
                Directory.CreateDirectory(playlistPath);

            if (!Directory.Exists(commercialsPath))
                Directory.CreateDirectory(commercialsPath);
        }

        public override string ToString() => DisplayName;
    }
}
