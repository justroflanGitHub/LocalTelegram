using MessageService.Models;
using StackExchange.Redis;
using System.Text.Json;

namespace MessageService.Services;

public interface IRedisService
{
    Task CacheRecentMessageAsync(long chatId, MessageDto message);
    Task<MessageDto?> GetRecentMessageAsync(long chatId);
    Task SetUserOnlineAsync(long userId, string connectionId);
    Task SetUserOfflineAsync(long userId, string connectionId);
    Task<bool> IsUserOnlineAsync(long userId);
    Task<List<string>> GetUserConnectionsAsync(long userId);
    Task SubscribeToChatAsync(long chatId, string connectionId);
    Task UnsubscribeFromChatAsync(long chatId, string connectionId);
}

public class RedisService : IRedisService, IDisposable
{
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisService> _logger;

    public RedisService(string connectionString, ILogger<RedisService> logger)
    {
        _logger = logger;
        
        try
        {
            _redis = ConnectionMultiplexer.Connect(connectionString);
            _db = _redis.GetDatabase();
            _logger.LogInformation("Connected to Redis");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Redis");
            throw;
        }
    }

    public async Task CacheRecentMessageAsync(long chatId, MessageDto message)
    {
        var key = $"chat:recent:{chatId}";
        var json = JsonSerializer.Serialize(message);
        await _db.StringSetAsync(key, json, TimeSpan.FromHours(24));
    }

    public async Task<MessageDto?> GetRecentMessageAsync(long chatId)
    {
        var key = $"chat:recent:{chatId}";
        var json = await _db.StringGetAsync(key);
        
        if (json.IsNullOrEmpty) return null;
        
        return JsonSerializer.Deserialize<MessageDto>(json!);
    }

    public async Task SetUserOnlineAsync(long userId, string connectionId)
    {
        var key = $"user:connections:{userId}";
        await _db.SetAddAsync(key, connectionId);
        await _db.KeyExpireAsync(key, TimeSpan.FromHours(12));
        
        // Also mark user as online
        var onlineKey = $"user:online:{userId}";
        await _db.StringSetAsync(onlineKey, "1", TimeSpan.FromHours(12));
    }

    public async Task SetUserOfflineAsync(long userId, string connectionId)
    {
        var key = $"user:connections:{userId}";
        await _db.SetRemoveAsync(key, connectionId);
        
        // Check if user has any remaining connections
        var count = await _db.SetLengthAsync(key);
        if (count == 0)
        {
            var onlineKey = $"user:online:{userId}";
            await _db.KeyDeleteAsync(onlineKey);
        }
    }

    public async Task<bool> IsUserOnlineAsync(long userId)
    {
        var key = $"user:online:{userId}";
        return await _db.KeyExistsAsync(key);
    }

    public async Task<List<string>> GetUserConnectionsAsync(long userId)
    {
        var key = $"user:connections:{userId}";
        var values = await _db.SetMembersAsync(key);
        return values.Select(v => v.ToString()).ToList();
    }

    public async Task SubscribeToChatAsync(long chatId, string connectionId)
    {
        var key = $"chat:subscribers:{chatId}";
        await _db.SetAddAsync(key, connectionId);
    }

    public async Task UnsubscribeFromChatAsync(long chatId, string connectionId)
    {
        var key = $"chat:subscribers:{chatId}";
        await _db.SetRemoveAsync(key, connectionId);
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}
