using StackExchange.Redis;
using System.Text.Json;

namespace PushService.Services;

public interface IRedisService
{
    Task RegisterConnectionAsync(long userId, string connectionId, string deviceType);
    Task UnregisterConnectionAsync(long userId, string connectionId);
    Task<List<string>> GetUserConnectionsAsync(long userId);
    Task<bool> HasActiveConnectionsAsync(long userId);
    Task SetUserOnlineAsync(long userId);
    Task SetUserOfflineAsync(long userId);
    Task UpdateUserStatusAsync(long userId, string status);
    Task UpdateLastHeartbeatAsync(long userId, string connectionId);
    Task SubscribeToChatAsync(long userId, long chatId, string connectionId);
    Task UnsubscribeFromChatAsync(long userId, long chatId, string connectionId);
    Task<List<long>> GetContactUserIdsAsync(long userId);
    Task CacheNotificationAsync(Notification notification);
    Task<List<Notification>> GetCachedNotificationsAsync(long userId);
}

public class RedisService : IRedisService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisService> _logger;

    public RedisService(IConnectionMultiplexer redis, ILogger<RedisService> logger)
    {
        _redis = redis;
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task RegisterConnectionAsync(long userId, string connectionId, string deviceType)
    {
        var batch = _db.CreateBatch();

        // Add connection to user's connection set
        batch.SetAddAsync($"user:{userId}:connections", connectionId);
        
        // Store connection metadata
        batch.HashSetAsync($"connection:{connectionId}", new[]
        {
            new HashEntry("userId", userId),
            new HashEntry("deviceType", deviceType),
            new HashEntry("connectedAt", DateTime.UtcNow.ToString("O"))
        });

        // Set expiration for connection metadata (24 hours)
        batch.KeyExpireAsync($"connection:{connectionId}", TimeSpan.FromHours(24));

        batch.Execute();
        await Task.WhenAll();

        _logger.LogDebug("Registered connection {ConnectionId} for user {UserId}", connectionId, userId);
    }

    public async Task UnregisterConnectionAsync(long userId, string connectionId)
    {
        var batch = _db.CreateBatch();

        batch.SetRemoveAsync($"user:{userId}:connections", connectionId);
        batch.KeyDeleteAsync($"connection:{connectionId}");

        batch.Execute();
        await Task.WhenAll();

        _logger.LogDebug("Unregistered connection {ConnectionId} for user {UserId}", connectionId, userId);
    }

    public async Task<List<string>> GetUserConnectionsAsync(long userId)
    {
        var connections = await _db.SetMembersAsync($"user:{userId}:connections");
        return connections.Select(c => c.ToString()).ToList();
    }

    public async Task<bool> HasActiveConnectionsAsync(long userId)
    {
        var count = await _db.SetLengthAsync($"user:{userId}:connections");
        return count > 0;
    }

    public async Task SetUserOnlineAsync(long userId)
    {
        await _db.HashSetAsync($"user:{userId}:status", new[]
        {
            new HashEntry("status", "online"),
            new HashEntry("lastSeen", DateTime.UtcNow.ToString("O"))
        });
    }

    public async Task SetUserOfflineAsync(long userId)
    {
        await _db.HashSetAsync($"user:{userId}:status", new[]
        {
            new HashEntry("status", "offline"),
            new HashEntry("lastSeen", DateTime.UtcNow.ToString("O"))
        });
    }

    public async Task UpdateUserStatusAsync(long userId, string status)
    {
        await _db.HashSetAsync($"user:{userId}:status", new[]
        {
            new HashEntry("status", status),
            new HashEntry("lastSeen", DateTime.UtcNow.ToString("O"))
        });
    }

    public async Task UpdateLastHeartbeatAsync(long userId, string connectionId)
    {
        await _db.HashSetAsync($"connection:{connectionId}", "lastHeartbeat", DateTime.UtcNow.ToString("O"));
    }

    public async Task SubscribeToChatAsync(long userId, long chatId, string connectionId)
    {
        await _db.SetAddAsync($"user:{userId}:chats", chatId);
        await _db.SetAddAsync($"chat:{chatId}:subscribers", connectionId);
    }

    public async Task UnsubscribeFromChatAsync(long userId, long chatId, string connectionId)
    {
        await _db.SetRemoveAsync($"chat:{chatId}:subscribers", connectionId);
    }

    public async Task<List<long>> GetContactUserIdsAsync(long userId)
    {
        // Get all chats the user is a member of
        var chatIds = await _db.SetMembersAsync($"user:{userId}:chats");
        
        var contactUserIds = new HashSet<long>();
        
        foreach (var chatId in chatIds)
        {
            // For each chat, get the chat members from the chat members set
            // This would typically be populated when users join chats
            var chatMemberKey = $"chat:{chatId}:members";
            var memberIds = await _db.SetMembersAsync(chatMemberKey);
            
            foreach (var memberId in memberIds)
            {
                if (long.TryParse(memberId, out var id) && id != userId)
                {
                    contactUserIds.Add(id);
                }
            }
        }

        return contactUserIds.ToList();
    }

    public async Task CacheNotificationAsync(Notification notification)
    {
        var json = JsonSerializer.Serialize(notification);
        await _db.ListLeftPushAsync($"user:{notification.UserId}:notifications", json);
        
        // Keep only last 100 notifications
        await _db.ListTrimAsync($"user:{notification.UserId}:notifications", 0, 99);
    }

    public async Task<List<Notification>> GetCachedNotificationsAsync(long userId)
    {
        var notifications = await _db.ListRangeAsync($"user:{userId}:notifications", 0, 49);
        var result = new List<Notification>();

        foreach (var notification in notifications)
        {
            try
            {
                var deserialized = JsonSerializer.Deserialize<Notification>(notification!);
                if (deserialized != null)
                {
                    result.Add(deserialized);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize notification");
            }
        }

        return result;
    }
}

public class Notification
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
