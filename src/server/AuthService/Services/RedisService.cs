using StackExchange.Redis;

namespace AuthService.Services;

public interface IRedisService
{
    Task CacheSessionAsync(Guid sessionId, long userId, DateTime expiresAt);
    Task<bool> IsSessionValidAsync(Guid sessionId);
    Task InvalidateSessionAsync(Guid sessionId);
    Task SetUserOnlineAsync(long userId);
    Task SetUserOfflineAsync(long userId);
    Task<bool> IsUserOnlineAsync(long userId);
}

public class RedisService : IRedisService, IDisposable
{
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisService> _logger;

    public RedisService(string connectionString, ILogger<RedisService>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RedisService>.Instance;
        
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

    public async Task CacheSessionAsync(Guid sessionId, long userId, DateTime expiresAt)
    {
        var key = $"session:{sessionId}";
        var ttl = expiresAt - DateTime.UtcNow;
        
        await _db.StringSetAsync(key, userId.ToString(), ttl > TimeSpan.Zero ? ttl : TimeSpan.FromHours(24));
        _logger.LogDebug("Cached session {SessionId} for user {UserId}", sessionId, userId);
    }

    public async Task<bool> IsSessionValidAsync(Guid sessionId)
    {
        var key = $"session:{sessionId}";
        return await _db.KeyExistsAsync(key);
    }

    public async Task InvalidateSessionAsync(Guid sessionId)
    {
        var key = $"session:{sessionId}";
        await _db.KeyDeleteAsync(key);
        _logger.LogDebug("Invalidated session {SessionId}", sessionId);
    }

    public async Task SetUserOnlineAsync(long userId)
    {
        var key = $"user:online:{userId}";
        await _db.StringSetAsync(key, "1", TimeSpan.FromMinutes(5));
    }

    public async Task SetUserOfflineAsync(long userId)
    {
        var key = $"user:online:{userId}";
        await _db.KeyDeleteAsync(key);
    }

    public async Task<bool> IsUserOnlineAsync(long userId)
    {
        var key = $"user:online:{userId}";
        return await _db.KeyExistsAsync(key);
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}
