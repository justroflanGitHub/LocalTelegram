using StackExchange.Redis;
using System.Text.Json;

namespace PushService.Services;

public interface IChatMuteService
{
    Task MuteChatAsync(long userId, long chatId, TimeSpan? duration = null);
    Task UnmuteChatAsync(long userId, long chatId);
    Task<bool> IsChatMutedAsync(long userId, long chatId);
    Task<ChatMuteSettings?> GetMuteSettingsAsync(long userId, long chatId);
    Task<List<ChatMuteSettings>> GetMutedChatsAsync(long userId);
    Task MuteAllChatsAsync(long userId, TimeSpan? duration = null);
    Task UnmuteAllChatsAsync(long userId);
}

public interface IBadgeCountService
{
    Task IncrementBadgeCountAsync(long userId, long chatId, int count = 1);
    Task DecrementBadgeCountAsync(long userId, long chatId, int count = 1);
    Task ResetBadgeCountAsync(long userId, long chatId);
    Task<int> GetChatBadgeCountAsync(long userId, long chatId);
    Task<int> GetTotalBadgeCountAsync(long userId);
    Task<Dictionary<long, int>> GetAllBadgeCountsAsync(long userId);
    Task ResetAllBadgeCountsAsync(long userId);
}

public class ChatMute
{
    public long UserId { get; set; }
    public long ChatId { get; set; }
    public DateTime MutedAt { get; set; }
    public DateTime? MutedUntil { get; set; }
    public bool MuteForever { get; set; }
}

public class ChatMuteSettings
{
    public long ChatId { get; set; }
    public bool IsMuted { get; set; }
    public DateTime? MutedUntil { get; set; }
    public bool IsForever { get; set; }
}

public class ChatMuteService : IChatMuteService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<ChatMuteService> _logger;
    private const string MutePrefix = "mute:";
    private const string UserMutesPrefix = "user_mutes:";

    public ChatMuteService(IConnectionMultiplexer redis, ILogger<ChatMuteService> logger)
    {
        _redis = redis;
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task MuteChatAsync(long userId, long chatId, TimeSpan? duration = null)
    {
        try
        {
            var muteKey = $"{MutePrefix}{userId}:{chatId}";
            var userMutesKey = $"{UserMutesPrefix}{userId}";

            var mute = new ChatMute
            {
                UserId = userId,
                ChatId = chatId,
                MutedAt = DateTime.UtcNow,
                MutedUntil = duration.HasValue ? DateTime.UtcNow.Add(duration.Value) : null,
                MuteForever = !duration.HasValue
            };

            var json = JsonSerializer.Serialize(mute);

            if (duration.HasValue)
            {
                await _db.StringSetAsync(muteKey, json, duration.Value);
            }
            else
            {
                await _db.StringSetAsync(muteKey, json);
            }

            // Add to user's muted chats set
            await _db.SetAddAsync(userMutesKey, chatId.ToString());

            _logger.LogInformation("User {UserId} muted chat {ChatId} for {Duration}", 
                userId, chatId, duration?.ToString() ?? "forever");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mute chat");
        }
    }

    public async Task UnmuteChatAsync(long userId, long chatId)
    {
        try
        {
            var muteKey = $"{MutePrefix}{userId}:{chatId}";
            var userMutesKey = $"{UserMutesPrefix}{userId}";

            await _db.KeyDeleteAsync(muteKey);
            await _db.SetRemoveAsync(userMutesKey, chatId.ToString());

            _logger.LogInformation("User {UserId} unmuted chat {ChatId}", userId, chatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unmute chat");
        }
    }

    public async Task<bool> IsChatMutedAsync(long userId, long chatId)
    {
        try
        {
            var muteKey = $"{MutePrefix}{userId}:{chatId}";
            var value = await _db.StringGetAsync(muteKey);

            if (!value.HasValue)
            {
                return false;
            }

            var mute = JsonSerializer.Deserialize<ChatMute>(value!);
            if (mute == null)
            {
                return false;
            }

            // Check if mute has expired
            if (!mute.MuteForever && mute.MutedUntil.HasValue && mute.MutedUntil.Value < DateTime.UtcNow)
            {
                // Mute has expired, remove it
                await UnmuteChatAsync(userId, chatId);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check mute status");
            return false;
        }
    }

    public async Task<ChatMuteSettings?> GetMuteSettingsAsync(long userId, long chatId)
    {
        try
        {
            var muteKey = $"{MutePrefix}{userId}:{chatId}";
            var value = await _db.StringGetAsync(muteKey);

            if (!value.HasValue)
            {
                return new ChatMuteSettings { ChatId = chatId, IsMuted = false };
            }

            var mute = JsonSerializer.Deserialize<ChatMute>(value!);
            if (mute == null)
            {
                return new ChatMuteSettings { ChatId = chatId, IsMuted = false };
            }

            // Check if mute has expired
            if (!mute.MuteForever && mute.MutedUntil.HasValue && mute.MutedUntil.Value < DateTime.UtcNow)
            {
                await UnmuteChatAsync(userId, chatId);
                return new ChatMuteSettings { ChatId = chatId, IsMuted = false };
            }

            return new ChatMuteSettings
            {
                ChatId = chatId,
                IsMuted = true,
                MutedUntil = mute.MutedUntil,
                IsForever = mute.MuteForever
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get mute settings");
            return null;
        }
    }

    public async Task<List<ChatMuteSettings>> GetMutedChatsAsync(long userId)
    {
        try
        {
            var userMutesKey = $"{UserMutesPrefix}{userId}";
            var mutedChatIds = await _db.SetMembersAsync(userMutesKey);

            var result = new List<ChatMuteSettings>();

            foreach (var chatIdStr in mutedChatIds)
            {
                if (long.TryParse(chatIdStr, out var chatId))
                {
                    var settings = await GetMuteSettingsAsync(userId, chatId);
                    if (settings != null && settings.IsMuted)
                    {
                        result.Add(settings);
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get muted chats");
            return new List<ChatMuteSettings>();
        }
    }

    public async Task MuteAllChatsAsync(long userId, TimeSpan? duration = null)
    {
        // This would require getting all user's chats from MessageService
        // For now, we'll implement a global mute flag
        try
        {
            var globalMuteKey = $"{MutePrefix}{userId}:global";
            var globalMute = new
            {
                MutedAt = DateTime.UtcNow,
                MutedUntil = duration.HasValue ? DateTime.UtcNow.Add(duration.Value) : (DateTime?)null,
                MuteForever = !duration.HasValue
            };

            var json = JsonSerializer.Serialize(globalMute);

            if (duration.HasValue)
            {
                await _db.StringSetAsync(globalMuteKey, json, duration.Value);
            }
            else
            {
                await _db.StringSetAsync(globalMuteKey, json);
            }

            _logger.LogInformation("User {UserId} muted all chats for {Duration}", 
                userId, duration?.ToString() ?? "forever");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mute all chats");
        }
    }

    public async Task UnmuteAllChatsAsync(long userId)
    {
        try
        {
            var globalMuteKey = $"{MutePrefix}{userId}:global";
            await _db.KeyDeleteAsync(globalMuteKey);

            _logger.LogInformation("User {UserId} unmuted all chats", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unmute all chats");
        }
    }
}

public class BadgeCountService : IBadgeCountService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<BadgeCountService> _logger;
    private const string BadgePrefix = "badge:";
    private const string UserBadgesPrefix = "user_badges:";

    public BadgeCountService(IConnectionMultiplexer redis, ILogger<BadgeCountService> logger)
    {
        _redis = redis;
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task IncrementBadgeCountAsync(long userId, long chatId, int count = 1)
    {
        try
        {
            var badgeKey = $"{BadgePrefix}{userId}:{chatId}";
            var userBadgesKey = $"{UserBadgesPrefix}{userId}";

            await _db.StringIncrementAsync(badgeKey, count);

            // Add to user's badge set
            await _db.SetAddAsync(userBadgesKey, chatId.ToString());

            // Set expiration (30 days)
            await _db.KeyExpireAsync(badgeKey, TimeSpan.FromDays(30));

            _logger.LogDebug("Incremented badge count for user {UserId} chat {ChatId} by {Count}", 
                userId, chatId, count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increment badge count");
        }
    }

    public async Task DecrementBadgeCountAsync(long userId, long chatId, int count = 1)
    {
        try
        {
            var badgeKey = $"{BadgePrefix}{userId}:{chatId}";

            var newValue = await _db.StringDecrementAsync(badgeKey, count);

            // If count is 0 or less, remove the key
            if (newValue <= 0)
            {
                await ResetBadgeCountAsync(userId, chatId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrement badge count");
        }
    }

    public async Task ResetBadgeCountAsync(long userId, long chatId)
    {
        try
        {
            var badgeKey = $"{BadgePrefix}{userId}:{chatId}";
            var userBadgesKey = $"{UserBadgesPrefix}{userId}";

            await _db.KeyDeleteAsync(badgeKey);
            await _db.SetRemoveAsync(userBadgesKey, chatId.ToString());

            _logger.LogDebug("Reset badge count for user {UserId} chat {ChatId}", userId, chatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset badge count");
        }
    }

    public async Task<int> GetChatBadgeCountAsync(long userId, long chatId)
    {
        try
        {
            var badgeKey = $"{BadgePrefix}{userId}:{chatId}";
            var value = await _db.StringGetAsync(badgeKey);

            return value.HasValue ? (int)value : 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get chat badge count");
            return 0;
        }
    }

    public async Task<int> GetTotalBadgeCountAsync(long userId)
    {
        try
        {
            var userBadgesKey = $"{UserBadgesPrefix}{userId}";
            var chatIds = await _db.SetMembersAsync(userBadgesKey);

            int total = 0;

            foreach (var chatIdStr in chatIds)
            {
                if (long.TryParse(chatIdStr, out var chatId))
                {
                    total += await GetChatBadgeCountAsync(userId, chatId);
                }
            }

            return total;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get total badge count");
            return 0;
        }
    }

    public async Task<Dictionary<long, int>> GetAllBadgeCountsAsync(long userId)
    {
        try
        {
            var userBadgesKey = $"{UserBadgesPrefix}{userId}";
            var chatIds = await _db.SetMembersAsync(userBadgesKey);

            var result = new Dictionary<long, int>();

            foreach (var chatIdStr in chatIds)
            {
                if (long.TryParse(chatIdStr, out var chatId))
                {
                    var count = await GetChatBadgeCountAsync(userId, chatId);
                    if (count > 0)
                    {
                        result[chatId] = count;
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all badge counts");
            return new Dictionary<long, int>();
        }
    }

    public async Task ResetAllBadgeCountsAsync(long userId)
    {
        try
        {
            var userBadgesKey = $"{UserBadgesPrefix}{userId}";
            var chatIds = await _db.SetMembersAsync(userBadgesKey);

            foreach (var chatIdStr in chatIds)
            {
                if (long.TryParse(chatIdStr, out var chatId))
                {
                    var badgeKey = $"{BadgePrefix}{userId}:{chatId}";
                    await _db.KeyDeleteAsync(badgeKey);
                }
            }

            await _db.KeyDeleteAsync(userBadgesKey);

            _logger.LogInformation("Reset all badge counts for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset all badge counts");
        }
    }
}
