using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Logging;

namespace AirStudio.CommercialManager.Core.Services.Security
{
    /// <summary>
    /// Service for handling user authorization and security checks
    /// </summary>
    public static class SecurityService
    {
        /// <summary>
        /// Get the current Windows user name (DOMAIN\username or MACHINE\username)
        /// </summary>
        public static string GetCurrentUserName()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                return identity?.Name ?? string.Empty;
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to get current user name", ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// Check if the current user is a Windows Local Administrator
        /// </summary>
        public static bool IsCurrentUserAdmin()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to check admin status", ex);
                return false;
            }
        }

        /// <summary>
        /// Check if a user is authorized to run the application
        /// </summary>
        /// <param name="config">Application configuration with allowed users list</param>
        /// <param name="username">Username to check (optional, defaults to current user)</param>
        /// <returns>Authorization result with details</returns>
        public static AuthorizationResult CheckAuthorization(AppConfiguration config, string username = null)
        {
            username = username ?? GetCurrentUserName();

            if (string.IsNullOrEmpty(username))
            {
                return AuthorizationResult.Denied("Unable to determine current user identity.");
            }

            // If no allowed users configured, allow all (for initial setup by admins)
            if (config?.AllowedUsers == null || config.AllowedUsers.Count == 0)
            {
                // Only allow if user is admin (for initial setup)
                if (IsCurrentUserAdmin())
                {
                    LogService.Info($"No allowed users configured. Admin '{username}' granted access for initial setup.");
                    return AuthorizationResult.Allowed(username, isAdmin: true, isInitialSetup: true);
                }
                else
                {
                    LogService.Warning($"No allowed users configured and '{username}' is not an administrator.");
                    return AuthorizationResult.Denied(
                        $"No authorized users have been configured.\n\n" +
                        $"Please contact an administrator to configure the application.\n\n" +
                        $"Current user: {username}");
                }
            }

            // Check if user is in allowed list
            bool isAllowed = config.IsUserAllowed(username);
            bool isAdmin = IsCurrentUserAdmin();

            if (isAllowed)
            {
                LogService.Info($"User '{username}' authorized (Admin: {isAdmin})");
                return AuthorizationResult.Allowed(username, isAdmin);
            }

            // Also check without domain prefix for flexibility
            var usernameWithoutDomain = GetUsernameWithoutDomain(username);
            if (!string.IsNullOrEmpty(usernameWithoutDomain) && config.IsUserAllowed(usernameWithoutDomain))
            {
                LogService.Info($"User '{username}' authorized via short name '{usernameWithoutDomain}' (Admin: {isAdmin})");
                return AuthorizationResult.Allowed(username, isAdmin);
            }

            LogService.Warning($"User '{username}' is not in the allowed users list");
            return AuthorizationResult.Denied(
                $"You are not authorized to use this application.\n\n" +
                $"Current user: {username}\n\n" +
                $"Please contact an administrator if you believe this is an error.");
        }

        /// <summary>
        /// Get username without domain prefix
        /// </summary>
        private static string GetUsernameWithoutDomain(string fullUsername)
        {
            if (string.IsNullOrEmpty(fullUsername))
                return null;

            var parts = fullUsername.Split('\\');
            return parts.Length > 1 ? parts[1] : fullUsername;
        }

        /// <summary>
        /// Normalize a username for storage (ensure consistent format)
        /// </summary>
        public static string NormalizeUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return null;

            return username.Trim();
        }

        /// <summary>
        /// Validate a username format
        /// </summary>
        public static bool IsValidUsernameFormat(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            // Allow formats: DOMAIN\user, MACHINE\user, or just user
            // Must not contain invalid characters
            var invalidChars = new[] { '/', ':', '*', '?', '"', '<', '>', '|', '\t', '\n', '\r' };
            return !username.Any(c => invalidChars.Contains(c));
        }
    }

    /// <summary>
    /// Result of an authorization check
    /// </summary>
    public class AuthorizationResult
    {
        /// <summary>
        /// Whether the user is authorized
        /// </summary>
        public bool IsAuthorized { get; private set; }

        /// <summary>
        /// The username that was checked
        /// </summary>
        public string Username { get; private set; }

        /// <summary>
        /// Whether the user is a Windows Local Administrator
        /// </summary>
        public bool IsAdmin { get; private set; }

        /// <summary>
        /// Whether this is initial setup mode (no users configured)
        /// </summary>
        public bool IsInitialSetup { get; private set; }

        /// <summary>
        /// Message explaining the result (especially for denied access)
        /// </summary>
        public string Message { get; private set; }

        private AuthorizationResult() { }

        public static AuthorizationResult Allowed(string username, bool isAdmin, bool isInitialSetup = false)
        {
            return new AuthorizationResult
            {
                IsAuthorized = true,
                Username = username,
                IsAdmin = isAdmin,
                IsInitialSetup = isInitialSetup,
                Message = "Access granted"
            };
        }

        public static AuthorizationResult Denied(string message)
        {
            return new AuthorizationResult
            {
                IsAuthorized = false,
                Username = SecurityService.GetCurrentUserName(),
                IsAdmin = false,
                IsInitialSetup = false,
                Message = message
            };
        }
    }
}
