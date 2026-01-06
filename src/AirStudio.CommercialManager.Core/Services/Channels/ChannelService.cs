using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Configuration;
using AirStudio.CommercialManager.Core.Services.Database;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Core.Services.Channels
{
    /// <summary>
    /// Service for loading and managing channels from the database
    /// </summary>
    public class ChannelService
    {
        private static readonly Lazy<ChannelService> _instance = new Lazy<ChannelService>(() => new ChannelService());
        public static ChannelService Instance => _instance.Value;

        private const string DATABASE_NAME = "air_virtual_studio";
        private const string CHANNELS_TABLE = "setup_channels";

        private List<Channel> _cachedChannels = new List<Channel>();
        private DateTime _lastRefresh = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

        private ChannelService() { }

        /// <summary>
        /// Load all channels from the database using parallel-first-success strategy
        /// </summary>
        public async Task<List<Channel>> LoadChannelsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var router = DatabaseRouter.Instance;
                var sql = $"SELECT Channel FROM {CHANNELS_TABLE} ORDER BY Channel";

                var result = await router.ReadFirstSuccessAsync(
                    DATABASE_NAME,
                    sql,
                    reader =>
                    {
                        var channels = new List<Channel>();
                        // Use do-while because the reader is already positioned on the first row
                        // by ReadFirstSuccessAsync before passing to the mapper
                        do
                        {
                            var channelName = reader.GetStringOrEmpty("Channel");
                            if (!string.IsNullOrWhiteSpace(channelName))
                            {
                                channels.Add(new Channel { Name = channelName });
                            }
                        } while (reader.Read());
                        return channels;
                    },
                    cancellationToken: cancellationToken);

                if (result.Success && result.Data != null)
                {
                    _cachedChannels = result.Data;
                    _lastRefresh = DateTime.Now;

                    // Merge with configured locations
                    MergeWithConfiguredLocations();

                    LogService.Info($"Loaded {_cachedChannels.Count} channels from database (profile: {result.ProfileName})");
                    return _cachedChannels;
                }
                else
                {
                    LogService.Warning($"Failed to load channels: {result.ErrorMessage}");
                    return _cachedChannels; // Return cached if available
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Exception loading channels", ex);
                return _cachedChannels;
            }
        }

        /// <summary>
        /// Get channels (from cache if valid, otherwise reload)
        /// </summary>
        public async Task<List<Channel>> GetChannelsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            if (forceRefresh || DateTime.Now - _lastRefresh > _cacheExpiry || _cachedChannels.Count == 0)
            {
                return await LoadChannelsAsync(cancellationToken);
            }
            return _cachedChannels;
        }

        /// <summary>
        /// Get a specific channel by name
        /// </summary>
        public async Task<Channel> GetChannelAsync(string channelName, CancellationToken cancellationToken = default)
        {
            var channels = await GetChannelsAsync(cancellationToken: cancellationToken);
            return channels.Find(c => string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get only usable channels (those with configured X targets)
        /// </summary>
        public async Task<List<Channel>> GetUsableChannelsAsync(CancellationToken cancellationToken = default)
        {
            var channels = await GetChannelsAsync(cancellationToken: cancellationToken);
            return channels.FindAll(c => c.IsUsable);
        }

        /// <summary>
        /// Merge loaded channels with configured locations from settings
        /// </summary>
        private void MergeWithConfiguredLocations()
        {
            var config = ConfigurationService.Instance.CurrentConfig;
            if (config?.ChannelLocations == null) return;

            foreach (var channel in _cachedChannels)
            {
                var location = config.GetChannelLocation(channel.Name);
                if (location != null)
                {
                    channel.XRootTargets = location.XRootTargets;
                }
            }

            // Add any channels from config that aren't in DB (for offline use)
            foreach (var location in config.ChannelLocations)
            {
                var exists = _cachedChannels.Exists(c =>
                    string.Equals(c.Name, location.Channel, StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    _cachedChannels.Add(new Channel
                    {
                        Name = location.Channel,
                        XRootTargets = location.XRootTargets,
                        IsFromConfig = true
                    });
                }
            }
        }

        /// <summary>
        /// Invalidate the cache (call when configuration changes)
        /// </summary>
        public void InvalidateCache()
        {
            _lastRefresh = DateTime.MinValue;
        }
    }
}
