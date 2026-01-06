using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AirStudio.CommercialManager.Core.Models
{
    /// <summary>
    /// Maps a channel to its X root target locations
    /// </summary>
    public class ChannelLocation
    {
        /// <summary>
        /// Channel name (from air_virtual_studio.setup_channels)
        /// </summary>
        public string Channel { get; set; } = string.Empty;

        /// <summary>
        /// List of X root targets (e.g., "X:\", "Y:\")
        /// Each target derives:
        /// - TAG folder: X:\Commercial Playlist\
        /// - Commercial audio folder: X:\Commercials\
        /// </summary>
        public List<string> XRootTargets { get; set; } = new List<string>();

        /// <summary>
        /// Returns true if this channel has at least one configured X root
        /// </summary>
        public bool IsConfigured => XRootTargets != null && XRootTargets.Count > 0;

        /// <summary>
        /// Get the TAG folder path for a specific X root
        /// </summary>
        public string GetTagFolder(string xRoot)
        {
            return Path.Combine(xRoot, "Commercial Playlist");
        }

        /// <summary>
        /// Get the Commercials audio folder path for a specific X root
        /// </summary>
        public string GetCommercialsFolder(string xRoot)
        {
            return Path.Combine(xRoot, "Commercials");
        }

        /// <summary>
        /// Get all TAG folder paths for this channel
        /// </summary>
        public IEnumerable<string> GetAllTagFolders()
        {
            return XRootTargets.Select(GetTagFolder);
        }

        /// <summary>
        /// Get all Commercials folder paths for this channel
        /// </summary>
        public IEnumerable<string> GetAllCommercialsFolders()
        {
            return XRootTargets.Select(GetCommercialsFolder);
        }

        /// <summary>
        /// Get the primary (first) X root target
        /// </summary>
        public string PrimaryXRoot => XRootTargets?.FirstOrDefault();

        /// <summary>
        /// Get the primary TAG folder
        /// </summary>
        public string PrimaryTagFolder => PrimaryXRoot != null ? GetTagFolder(PrimaryXRoot) : null;

        /// <summary>
        /// Get the primary Commercials folder
        /// </summary>
        public string PrimaryCommercialsFolder => PrimaryXRoot != null ? GetCommercialsFolder(PrimaryXRoot) : null;

        public override string ToString()
        {
            return $"{Channel} ({XRootTargets?.Count ?? 0} targets)";
        }
    }
}
