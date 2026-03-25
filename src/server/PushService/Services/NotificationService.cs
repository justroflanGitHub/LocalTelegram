using StackExchange.Redis;
using System.Text.Json;

namespace PushService.Services;

public interface INotificationService
{
    Task StoreNotificationAsync(Notification notification);
    Task<List<Notification>> GetPendingNotificationsAsync(long userId);
    Task MarkAsReadAsync(long notificationId, long userId);
    Task MarkAllAsReadAsync(long userId);
    Task<int> GetUnreadCountAsync(long userId);
    Task DeleteNotificationAsync(long notificationId, long userId);
}

public class NotificationService : INotificationService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<NotificationService> _logger;
    private const string NotificationPrefix = "notifications:";

    public NotificationService(IConnectionMultiplexer redis, ILogger<NotificationService> logger)
    {
        _redis = redis;
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task StoreNotificationAsync(Notification notification)
    {
        try
        {
            var json = JsonSerializer.Serialize(notification);
            var key = $"{NotificationPrefix}{notification.UserId}";

            // Add to list (newest first)
            await _db.ListLeftPushAsync(key, json);

            // Keep only last 100 notifications
            await _db.ListTrimAsync(key, 0, 99);

            // Set expiration (30 days)
            await _db.KeyExpireAsync(key, TimeSpan.FromDays(30));

            // Add to unread set
            await _db.SetAddAsync($"{key}:unread", notification.Id);

            _logger.LogDebug("Stored notification {NotificationId} for user {UserId}", 
                notification.Id, notification.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store notification");
        }
    }

    public async Task<List<Notification>> GetPendingNotificationsAsync(long userId)
    {
        try
        {
            var key = $"{NotificationPrefix}{userId}";
            var notifications = await _db.ListRangeAsync(key, 0, 49);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pending notifications");
            return new List<Notification>();
        }
    }

    public async Task MarkAsReadAsync(long notificationId, long userId)
    {
        try
        {
            var key = $"{NotificationPrefix}{userId}";
            var unreadKey = $"{key}:unread";

            // Remove from unread set
            await _db.SetRemoveAsync(unreadKey, notificationId);

            // Update notification in list
            var notifications = await _db.ListRangeAsync(key);
            for (int i = 0; i < notifications.Length; i++)
            {
                try
                {
                    var notification = JsonSerializer.Deserialize<Notification>(notifications[i]!);
                    if (notification != null && notification.Id == notificationId)
                    {
                        notification.IsRead = true;
                        await _db.ListSetByIndexAsync(key, i, JsonSerializer.Serialize(notification));
                        break;
                    }
                }
                catch (JsonException)
                {
                    // Skip invalid entries
                }
            }

            _logger.LogDebug("Marked notification {NotificationId} as read for user {UserId}", 
                notificationId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark notification as read");
        }
    }

    public async Task MarkAllAsReadAsync(long userId)
    {
        try
        {
            var key = $"{NotificationPrefix}{userId}";
            var unreadKey = $"{key}:unread";

            // Clear unread set
            await _db.KeyDeleteAsync(unreadKey);

            // Update all notifications in list
            var notifications = await _db.ListRangeAsync(key);
            for (int i = 0; i < notifications.Length; i++)
            {
                try
                {
                    var notification = JsonSerializer.Deserialize<Notification>(notifications[i]!);
                    if (notification != null && !notification.IsRead)
                    {
                        notification.IsRead = true;
                        await _db.ListSetByIndexAsync(key, i, JsonSerializer.Serialize(notification));
                    }
                }
                catch (JsonException)
                {
                    // Skip invalid entries
                }
            }

            _logger.LogDebug("Marked all notifications as read for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark all notifications as read");
        }
    }

    public async Task<int> GetUnreadCountAsync(long userId)
    {
        try
        {
            var unreadKey = $"{NotificationPrefix}{userId}:unread";
            var count = await _db.SetLengthAsync(unreadKey);
            return (int)count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get unread count");
            return 0;
        }
    }

    public async Task DeleteNotificationAsync(long notificationId, long userId)
    {
        try
        {
            var key = $"{NotificationPrefix}{userId}";
            var unreadKey = $"{key}:unread";

            // Remove from unread set
            await _db.SetRemoveAsync(unreadKey, notificationId);

            // Remove from list
            var notifications = await _db.ListRangeAsync(key);
            for (int i = notifications.Length - 1; i >= 0; i--)
            {
                try
                {
                    var notification = JsonSerializer.Deserialize<Notification>(notifications[i]!);
                    if (notification != null && notification.Id == notificationId)
                    {
                        await _db.ListRemoveAsync(key, notifications[i]);
                        break;
                    }
                }
                catch (JsonException)
                {
                    // Skip invalid entries
                }
            }

            _logger.LogDebug("Deleted notification {NotificationId} for user {UserId}", 
                notificationId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete notification");
        }
    }
}
