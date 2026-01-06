using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Core.Services.Configuration
{
    /// <summary>
    /// Service for managing application configuration
    /// - Stores in ProgramData (machine-wide)
    /// - Passwords encrypted with DPAPI at rest
    /// - Export/Import with AES encryption
    /// </summary>
    public class ConfigurationService
    {
        private static readonly object _lock = new object();
        private static ConfigurationService _instance;

        private readonly string _configDirectory;
        private readonly string _configFilePath;

        private AppConfiguration _currentConfig;

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static ConfigurationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new ConfigurationService();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Current loaded configuration
        /// </summary>
        public AppConfiguration CurrentConfig => _currentConfig;

        private ConfigurationService()
        {
            _configDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "AirStudio",
                "CommercialManager");

            _configFilePath = Path.Combine(_configDirectory, "config.json");
        }

        #region Load / Save

        /// <summary>
        /// Load configuration from ProgramData
        /// </summary>
        public async Task<AppConfiguration> LoadAsync()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    LogService.Info("No configuration file found, creating default configuration");
                    _currentConfig = AppConfiguration.CreateDefault();
                    await SaveAsync();
                    return _currentConfig;
                }

                string json = await Task.Run(() => File.ReadAllText(_configFilePath));
                var config = JsonConvert.DeserializeObject<AppConfiguration>(json);

                // Decrypt passwords (stored as DPAPI)
                if (config.DatabaseProfiles != null)
                {
                    foreach (var profile in config.DatabaseProfiles)
                    {
                        if (!string.IsNullOrEmpty(profile.Password))
                        {
                            try
                            {
                                profile.Password = SecureStorage.UnprotectDpapi(profile.Password);
                            }
                            catch
                            {
                                LogService.Warning($"Failed to decrypt password for profile: {profile.Name}");
                                profile.Password = string.Empty;
                            }
                        }
                    }
                }

                _currentConfig = config;
                LogService.Info($"Configuration loaded from {_configFilePath}");
                return _currentConfig;
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to load configuration", ex);
                _currentConfig = AppConfiguration.CreateDefault();
                return _currentConfig;
            }
        }

        /// <summary>
        /// Save configuration to ProgramData
        /// </summary>
        public async Task SaveAsync()
        {
            try
            {
                // Ensure directory exists
                Directory.CreateDirectory(_configDirectory);

                // Create a copy with encrypted passwords
                var configToSave = CloneConfiguration(_currentConfig);

                // Encrypt passwords with DPAPI
                if (configToSave.DatabaseProfiles != null)
                {
                    foreach (var profile in configToSave.DatabaseProfiles)
                    {
                        if (!string.IsNullOrEmpty(profile.Password))
                        {
                            profile.Password = SecureStorage.ProtectDpapi(profile.Password);
                        }
                    }
                }

                configToSave.LastModified = DateTime.UtcNow;

                string json = JsonConvert.SerializeObject(configToSave, Formatting.Indented);

                // Write to temp file first, then move (atomic operation)
                string tempPath = _configFilePath + ".tmp";
                await Task.Run(() => File.WriteAllText(tempPath, json));
                File.Move(tempPath, _configFilePath, true);

                LogService.Info($"Configuration saved to {_configFilePath}");
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to save configuration", ex);
                throw;
            }
        }

        #endregion

        #region Export / Import

        /// <summary>
        /// Export configuration to a JSON file with AES-encrypted passwords
        /// </summary>
        public async Task ExportAsync(string exportPath)
        {
            try
            {
                var exportConfig = CloneConfiguration(_currentConfig);

                // Convert passwords from plain to AES-encrypted for export
                if (exportConfig.DatabaseProfiles != null)
                {
                    foreach (var profile in exportConfig.DatabaseProfiles)
                    {
                        if (!string.IsNullOrEmpty(profile.Password))
                        {
                            profile.Password = SecureStorage.EncryptForExport(profile.Password);
                        }
                    }
                }

                // Add export metadata
                var exportWrapper = new
                {
                    ExportVersion = 1,
                    ExportDate = DateTime.UtcNow,
                    MachineName = Environment.MachineName,
                    Configuration = exportConfig
                };

                string json = JsonConvert.SerializeObject(exportWrapper, Formatting.Indented);
                await Task.Run(() => File.WriteAllText(exportPath, json));

                LogService.Info($"Configuration exported to {exportPath}");
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to export configuration", ex);
                throw;
            }
        }

        /// <summary>
        /// Import configuration from a JSON file, decrypting AES passwords and re-encrypting with DPAPI
        /// </summary>
        public async Task ImportAsync(string importPath)
        {
            try
            {
                // Create backup of current config
                string backupPath = _configFilePath + ".bak";
                if (File.Exists(_configFilePath))
                {
                    File.Copy(_configFilePath, backupPath, true);
                    LogService.Info($"Backup created at {backupPath}");
                }

                string json = await Task.Run(() => File.ReadAllText(importPath));
                var jObject = JObject.Parse(json);

                AppConfiguration importedConfig;

                // Check if this is a wrapped export or raw config
                if (jObject.ContainsKey("Configuration"))
                {
                    importedConfig = jObject["Configuration"].ToObject<AppConfiguration>();
                }
                else
                {
                    importedConfig = jObject.ToObject<AppConfiguration>();
                }

                // Decrypt AES passwords and keep them plain (will be DPAPI encrypted on save)
                if (importedConfig.DatabaseProfiles != null)
                {
                    foreach (var profile in importedConfig.DatabaseProfiles)
                    {
                        if (!string.IsNullOrEmpty(profile.Password))
                        {
                            try
                            {
                                profile.Password = SecureStorage.DecryptFromExport(profile.Password);
                            }
                            catch
                            {
                                LogService.Warning($"Failed to decrypt password for profile: {profile.Name}");
                                profile.Password = string.Empty;
                            }
                        }
                    }
                }

                _currentConfig = importedConfig;
                await SaveAsync();

                LogService.Info($"Configuration imported from {importPath}");
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to import configuration", ex);
                throw;
            }
        }

        #endregion

        #region Configuration Updates

        /// <summary>
        /// Update database profiles
        /// </summary>
        public async Task UpdateDatabaseProfilesAsync(List<DatabaseProfile> profiles)
        {
            _currentConfig.DatabaseProfiles = profiles ?? new List<DatabaseProfile>();

            // Ensure exactly one default
            var defaultProfile = _currentConfig.DatabaseProfiles.Find(p => p.IsDefault);
            if (defaultProfile == null && _currentConfig.DatabaseProfiles.Count > 0)
            {
                _currentConfig.DatabaseProfiles[0].IsDefault = true;
            }

            await SaveAsync();
        }

        /// <summary>
        /// Update channel locations
        /// </summary>
        public async Task UpdateChannelLocationsAsync(List<ChannelLocation> locations)
        {
            _currentConfig.ChannelLocations = locations ?? new List<ChannelLocation>();
            await SaveAsync();
        }

        /// <summary>
        /// Update allowed users list
        /// </summary>
        public async Task UpdateAllowedUsersAsync(List<string> users)
        {
            _currentConfig.AllowedUsers = users ?? new List<string>();
            await SaveAsync();
        }

        /// <summary>
        /// Update sequence base NN value
        /// </summary>
        public async Task UpdateSequenceBaseNNAsync(int value)
        {
            _currentConfig.SequenceBaseNN = value;
            await SaveAsync();
        }

        /// <summary>
        /// Set or update a single channel location
        /// </summary>
        public async Task SetChannelLocationAsync(string channel, List<string> xRootTargets)
        {
            var existing = _currentConfig.ChannelLocations?.Find(c =>
                string.Equals(c.Channel, channel, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.XRootTargets = xRootTargets ?? new List<string>();
            }
            else
            {
                if (_currentConfig.ChannelLocations == null)
                    _currentConfig.ChannelLocations = new List<ChannelLocation>();

                _currentConfig.ChannelLocations.Add(new ChannelLocation
                {
                    Channel = channel,
                    XRootTargets = xRootTargets ?? new List<string>()
                });
            }

            await SaveAsync();
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Get decrypted password for a database profile
        /// </summary>
        public string GetDecryptedPassword(DatabaseProfile profile)
        {
            // Passwords are already decrypted in memory after load
            return profile?.Password ?? string.Empty;
        }

        private AppConfiguration CloneConfiguration(AppConfiguration source)
        {
            var json = JsonConvert.SerializeObject(source);
            return JsonConvert.DeserializeObject<AppConfiguration>(json);
        }

        #endregion
    }
}
