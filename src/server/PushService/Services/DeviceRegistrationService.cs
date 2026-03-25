using StackExchange.Redis;
using System.Text.Json;

namespace PushService.Services;

public interface IDeviceRegistrationService
{
    Task RegisterDeviceAsync(long userId, string connectionId, string deviceType, string deviceInfo);
    Task UnregisterDeviceAsync(string connectionId);
    Task<List<DeviceInfo>> GetUserDevicesAsync(long userId);
    Task<DeviceInfo?> GetDeviceByConnectionIdAsync(string connectionId);
    Task UpdateLastSeenAsync(string connectionId);
}

public class DeviceRegistrationService : IDeviceRegistrationService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<DeviceRegistrationService> _logger;

    public DeviceRegistrationService(IConnectionMultiplexer redis, ILogger<DeviceRegistrationService> logger)
    {
        _redis = redis;
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task RegisterDeviceAsync(long userId, string connectionId, string deviceType, string deviceInfo)
    {
        try
        {
            var device = new DeviceInfo
            {
                ConnectionId = connectionId,
                UserId = userId,
                DeviceType = deviceType,
                DeviceInfo = deviceInfo,
                RegisteredAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(device);

            // Store device by connection ID
            await _db.StringSetAsync($"device:{connectionId}", json, TimeSpan.FromHours(24));

            // Add to user's devices set
            await _db.SetAddAsync($"user:{userId}:devices", connectionId);

            _logger.LogDebug("Registered device {DeviceType} for user {UserId}", deviceType, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register device");
        }
    }

    public async Task UnregisterDeviceAsync(string connectionId)
    {
        try
        {
            // Get device info first
            var deviceJson = await _db.StringGetAsync($"device:{connectionId}");
            if (deviceJson.IsNullOrEmpty)
            {
                return;
            }

            var device = JsonSerializer.Deserialize<DeviceInfo>(deviceJson!);
            if (device != null)
            {
                // Remove from user's devices set
                await _db.SetRemoveAsync($"user:{device.UserId}:devices", connectionId);
            }

            // Delete device info
            await _db.KeyDeleteAsync($"device:{connectionId}");

            _logger.LogDebug("Unregistered device {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister device");
        }
    }

    public async Task<List<DeviceInfo>> GetUserDevicesAsync(long userId)
    {
        try
        {
            var connectionIds = await _db.SetMembersAsync($"user:{userId}:devices");
            var devices = new List<DeviceInfo>();

            foreach (var connectionId in connectionIds)
            {
                var deviceJson = await _db.StringGetAsync($"device:{connectionId}");
                if (!deviceJson.IsNullOrEmpty)
                {
                    try
                    {
                        var device = JsonSerializer.Deserialize<DeviceInfo>(deviceJson!);
                        if (device != null)
                        {
                            devices.Add(device);
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip invalid entries
                    }
                }
            }

            return devices;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user devices");
            return new List<DeviceInfo>();
        }
    }

    public async Task<DeviceInfo?> GetDeviceByConnectionIdAsync(string connectionId)
    {
        try
        {
            var deviceJson = await _db.StringGetAsync($"device:{connectionId}");
            if (deviceJson.IsNullOrEmpty)
            {
                return null;
            }

            return JsonSerializer.Deserialize<DeviceInfo>(deviceJson!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get device by connection ID");
            return null;
        }
    }

    public async Task UpdateLastSeenAsync(string connectionId)
    {
        try
        {
            var deviceJson = await _db.StringGetAsync($"device:{connectionId}");
            if (deviceJson.IsNullOrEmpty)
            {
                return;
            }

            var device = JsonSerializer.Deserialize<DeviceInfo>(deviceJson!);
            if (device != null)
            {
                device.LastSeenAt = DateTime.UtcNow;
                await _db.StringSetAsync($"device:{connectionId}", JsonSerializer.Serialize(device), TimeSpan.FromHours(24));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update last seen");
        }
    }
}

public class DeviceInfo
{
    public string ConnectionId { get; set; } = string.Empty;
    public long UserId { get; set; }
    public string DeviceType { get; set; } = string.Empty;
    public string DeviceInfo { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}
