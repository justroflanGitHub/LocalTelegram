using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AuthService.Data;
using AuthService.Models;

namespace AuthService.Services
{
    /// <summary>
    /// Device management service for tracking and managing user sessions
    /// </summary>
    public class DeviceService
    {
        private readonly AuthDbContext _context;
        private readonly ILogger<DeviceService> _logger;

        public DeviceService(AuthDbContext context, ILogger<DeviceService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Registers a new device/session for a user
        /// </summary>
        public async Task<DeviceInfo> RegisterDeviceAsync(Guid userId, string userAgent, string ipAddress)
        {
            var deviceInfo = ParseUserAgent(userAgent);
            var location = await GetLocationFromIpAsync(ipAddress);

            var device = new Device
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DeviceName = deviceInfo.DeviceName,
                DeviceType = deviceInfo.DeviceType,
                Platform = deviceInfo.Platform,
                AppVersion = deviceInfo.AppVersion,
                IpAddress = ipAddress,
                Location = location,
                UserAgent = userAgent,
                LastActiveAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _context.Devices.Add(device);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Device registered for user {UserId}: {DeviceName}", userId, device.DeviceName);

            return MapToDeviceInfo(device);
        }

        /// <summary>
        /// Gets all active devices for a user
        /// </summary>
        public async Task<List<DeviceInfo>> GetUserDevicesAsync(Guid userId)
        {
            var devices = await _context.Devices
                .Where(d => d.UserId == userId && d.IsActive)
                .OrderByDescending(d => d.LastActiveAt)
                .ToListAsync();

            return devices.Select(MapToDeviceInfo).ToList();
        }

        /// <summary>
        /// Updates the last active timestamp for a device
        /// </summary>
        public async Task UpdateLastActiveAsync(Guid deviceId)
        {
            var device = await _context.Devices.FindAsync(deviceId);
            if (device != null)
            {
                device.LastActiveAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Revokes a specific device session
        /// </summary>
        public async Task<bool> RevokeDeviceAsync(Guid userId, Guid deviceId)
        {
            var device = await _context.Devices
                .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId);

            if (device == null)
            {
                return false;
            }

            device.IsActive = false;
            device.RevokedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Device revoked for user {UserId}: {DeviceId}", userId, deviceId);

            return true;
        }

        /// <summary>
        /// Revokes all other devices except the current one
        /// </summary>
        public async Task<int> RevokeOtherDevicesAsync(Guid userId, Guid currentDeviceId)
        {
            var devices = await _context.Devices
                .Where(d => d.UserId == userId && d.Id != currentDeviceId && d.IsActive)
                .ToListAsync();

            var now = DateTime.UtcNow;
            foreach (var device in devices)
            {
                device.IsActive = false;
                device.RevokedAt = now;
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Revoked {Count} devices for user {UserId}", devices.Count, userId);

            return devices.Count;
        }

        /// <summary>
        /// Revokes all devices for a user (useful for password reset)
        /// </summary>
        public async Task<int> RevokeAllDevicesAsync(Guid userId)
        {
            var devices = await _context.Devices
                .Where(d => d.UserId == userId && d.IsActive)
                .ToListAsync();

            var now = DateTime.UtcNow;
            foreach (var device in devices)
            {
                device.IsActive = false;
                device.RevokedAt = now;
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Revoked all {Count} devices for user {UserId}", devices.Count, userId);

            return devices.Count;
        }

        /// <summary>
        /// Gets device statistics for a user
        /// </summary>
        public async Task<DeviceStats> GetDeviceStatsAsync(Guid userId)
        {
            var devices = await _context.Devices
                .Where(d => d.UserId == userId)
                .ToListAsync();

            return new DeviceStats
            {
                TotalDevices = devices.Count,
                ActiveDevices = devices.Count(d => d.IsActive),
                DeviceTypes = devices
                    .GroupBy(d => d.DeviceType)
                    .ToDictionary(g => g.Key, g => g.Count()),
                Platforms = devices
                    .GroupBy(d => d.Platform)
                    .ToDictionary(g => g.Key, g => g.Count()),
                LastActiveAt = devices.Where(d => d.IsActive).Max(d => d.LastActiveAt)
            };
        }

        /// <summary>
        /// Cleans up inactive devices older than specified days
        /// </summary>
        public async Task<int> CleanupInactiveDevicesAsync(int olderThanDays = 90)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-olderThanDays);

            var devices = await _context.Devices
                .Where(d => !d.IsActive && d.RevokedAt < cutoffDate)
                .ToListAsync();

            _context.Devices.RemoveRange(devices);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Cleaned up {Count} inactive devices older than {Days} days", 
                devices.Count, olderThanDays);

            return devices.Count;
        }

        private DeviceParseResult ParseUserAgent(string userAgent)
        {
            var result = new DeviceParseResult();

            if (string.IsNullOrEmpty(userAgent))
            {
                result.DeviceName = "Unknown Device";
                result.DeviceType = "Unknown";
                return result;
            }

            // Parse platform
            if (userAgent.Contains("Windows"))
            {
                result.Platform = "Windows";
                result.DeviceType = "Desktop";
                result.DeviceName = "Windows PC";
            }
            else if (userAgent.Contains("Mac OS X"))
            {
                result.Platform = "macOS";
                result.DeviceType = "Desktop";
                result.DeviceName = "Mac";
            }
            else if (userAgent.Contains("Linux"))
            {
                result.Platform = "Linux";
                result.DeviceType = "Desktop";
                result.DeviceName = "Linux PC";
            }
            else if (userAgent.Contains("Android"))
            {
                result.Platform = "Android";
                result.DeviceType = "Mobile";
                // Try to extract device name
                var match = System.Text.RegularExpressions.Regex.Match(userAgent, @"Android[^;]+;\s*([^)]+)");
                if (match.Success)
                {
                    result.DeviceName = match.Groups[1].Value.Trim();
                }
                else
                {
                    result.DeviceName = "Android Device";
                }
            }
            else if (userAgent.Contains("iPhone"))
            {
                result.Platform = "iOS";
                result.DeviceType = "Mobile";
                result.DeviceName = "iPhone";
            }
            else if (userAgent.Contains("iPad"))
            {
                result.Platform = "iOS";
                result.DeviceType = "Tablet";
                result.DeviceName = "iPad";
            }
            else
            {
                result.Platform = "Unknown";
                result.DeviceType = "Unknown";
                result.DeviceName = "Unknown Device";
            }

            // Parse app version (custom header or user agent pattern)
            var versionMatch = System.Text.RegularExpressions.Regex.Match(userAgent, @"LocalTelegram/([\d.]+)");
            if (versionMatch.Success)
            {
                result.AppVersion = versionMatch.Groups[1].Value;
            }

            return result;
        }

        private async Task<string> GetLocationFromIpAsync(string ipAddress)
        {
            // Basic IP location lookup (can be enhanced with MaxMind GeoIP)
            if (string.IsNullOrEmpty(ipAddress) || ipAddress == "127.0.0.1" || ipAddress == "::1")
            {
                return "Local";
            }

            // For now, return Unknown. In production, integrate with GeoIP service
            return await Task.FromResult("Unknown");
        }

        private DeviceInfo MapToDeviceInfo(Device device)
        {
            return new DeviceInfo
            {
                Id = device.Id,
                DeviceName = device.DeviceName,
                DeviceType = device.DeviceType,
                Platform = device.Platform,
                AppVersion = device.AppVersion,
                IpAddress = device.IpAddress,
                Location = device.Location,
                LastActiveAt = device.LastActiveAt,
                CreatedAt = device.CreatedAt,
                IsActive = device.IsActive,
                IsCurrent = false // Set by caller if needed
            };
        }
    }

    #region Models

    public class Device
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string? AppVersion { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string? Location { get; set; }
        public string UserAgent { get; set; } = string.Empty;
        public DateTime LastActiveAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public DateTime? RevokedAt { get; set; }
    }

    public class DeviceInfo
    {
        public Guid Id { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string? AppVersion { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string? Location { get; set; }
        public DateTime LastActiveAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public bool IsCurrent { get; set; }
    }

    public class DeviceStats
    {
        public int TotalDevices { get; set; }
        public int ActiveDevices { get; set; }
        public Dictionary<string, int> DeviceTypes { get; set; } = new();
        public Dictionary<string, int> Platforms { get; set; } = new();
        public DateTime? LastActiveAt { get; set; }
    }

    internal class DeviceParseResult
    {
        public string DeviceName { get; set; } = "Unknown Device";
        public string DeviceType { get; set; } = "Unknown";
        public string Platform { get; set; } = "Unknown";
        public string? AppVersion { get; set; }
    }

    #endregion
}
