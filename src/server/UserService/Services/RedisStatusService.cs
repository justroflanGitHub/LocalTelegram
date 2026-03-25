using StackExchange.Redis;
using System.Text.Json;
using UserService.Models;

namespace UserService.Services;

public interface IRedisStatusService
{
    Task SetUserOnlineAsync(long userId);
    Task SetUserOfflineAsync(long userId);
    Task SetUserStatusAsync(long userId, UserStatus status);
    Task<UserStatus> GetUserStatusAsync(long userId);
    Task<DateTime?> GetLastSeenAsync(long userId);
    Task<Dictionary<long, UserStatus>> GetUsersStatusAsync(IEnumerable<long> userIds);
}

public class RedisStatusService : IRedisStatusService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisStatusService> _logger;

    public RedisStatusService(IConnectionMultiplexer redis, ILogger<RedisStatusService> logger)
    {
        _redis = redis;
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task SetUserOnlineAsync(long userId)
    {
        await _db.HashSetAsync($"user:{userId}:status", new[]
        {
            new HashEntry("status", ((int)UserStatus.Online).ToString()),
            new HashEntry("lastSeen", DateTime.UtcNow.ToString("O"))
        });
    }

    public async Task SetUserOfflineAsync(long userId)
    {
        await _db.HashSetAsync($"user:{userId}:status", new[]
        {
            new HashEntry("status", ((int)UserStatus.Offline).ToString()),
            new HashEntry("lastSeen", DateTime.UtcNow.ToString("O"))
        });
    }

    public async Task SetUserStatusAsync(long userId, UserStatus status)
    {
        await _db.HashSetAsync($"user:{userId}:status", new[]
        {
            new HashEntry("status", ((int)status).ToString()),
            new HashEntry("lastSeen", DateTime.UtcNow.ToString("O"))
        });
    }

    public async Task<UserStatus> GetUserStatusAsync(long userId)
    {
        var statusValue = await _db.HashGetAsync($"user:{userId}:status", "status");
        
        if (statusValue.IsNullOrEmpty)
        {
            return UserStatus.Offline;
        }

        if (int.TryParse(statusValue, out var statusInt) && Enum.IsDefined(typeof(UserStatus), statusInt))
        {
            return (UserStatus)statusInt;
        }

        return UserStatus.Offline;
    }

    public async Task<DateTime?> GetLastSeenAsync(long userId)
    {
        var lastSeenValue = await _db.HashGetAsync($"user:{userId}:status", "lastSeen");
        
        if (lastSeenValue.IsNullOrEmpty)
        {
            return null;
        }

        if (DateTime.TryParse(lastSeenValue, out var lastSeen))
        {
            return lastSeen;
        }

        return null;
    }

    public async Task<Dictionary<long, UserStatus>> GetUsersStatusAsync(IEnumerable<long> userIds)
    {
        var result = new Dictionary<long, UserStatus>();

        foreach (var userId in userIds)
        {
            result[userId] = await GetUserStatusAsync(userId);
        }

        return result;
    }
}
