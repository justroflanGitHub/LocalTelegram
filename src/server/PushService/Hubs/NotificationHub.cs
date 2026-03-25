using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PushService.Services;
using System.Security.Claims;

namespace PushService.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    private readonly IRedisService _redisService;
    private readonly INotificationService _notificationService;
    private readonly IDeviceRegistrationService _deviceService;
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(
        IRedisService redisService,
        INotificationService notificationService,
        IDeviceRegistrationService deviceService,
        ILogger<NotificationHub> logger)
    {
        _redisService = redisService;
        _notificationService = notificationService;
        _deviceService = deviceService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        if (userId == null)
        {
            _logger.LogWarning("Connection attempted without valid user ID");
            Context.Abort();
            return;
        }

        var deviceType = Context.GetHttpContext()?.Request.Headers["X-Device-Type"].ToString() ?? "unknown";
        var deviceInfo = Context.GetHttpContext()?.Request.Headers["User-Agent"].ToString() ?? "unknown";

        // Register connection
        await _redisService.RegisterConnectionAsync(userId.Value, Context.ConnectionId, deviceType);
        
        // Register device
        await _deviceService.RegisterDeviceAsync(userId.Value, Context.ConnectionId, deviceType, deviceInfo);

        _logger.LogInformation("User {UserId} connected with device {DeviceType} via connection {ConnectionId}", 
            userId, deviceType, Context.ConnectionId);

        // Send pending notifications
        var pendingNotifications = await _notificationService.GetPendingNotificationsAsync(userId.Value);
        foreach (var notification in pendingNotifications)
        {
            await Clients.Caller.SendAsync("Notification", notification);
        }

        // Notify contacts that user is online
        await _redisService.SetUserOnlineAsync(userId.Value);
        await NotifyContactsOfStatusAsync(userId.Value, true);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        if (userId != null)
        {
            await _redisService.UnregisterConnectionAsync(userId.Value, Context.ConnectionId);
            await _deviceService.UnregisterDeviceAsync(Context.ConnectionId);

            _logger.LogInformation("User {UserId} disconnected from connection {ConnectionId}", 
                userId, Context.ConnectionId);

            // Check if user has any other active connections
            var hasOtherConnections = await _redisService.HasActiveConnectionsAsync(userId.Value);
            if (!hasOtherConnections)
            {
                await _redisService.SetUserOfflineAsync(userId.Value);
                await NotifyContactsOfStatusAsync(userId.Value, false);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task SubscribeToChat(long chatId)
    {
        var userId = GetUserId();
        if (userId == null) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat:{chatId}");
        await _redisService.SubscribeToChatAsync(userId.Value, chatId, Context.ConnectionId);

        _logger.LogDebug("User {UserId} subscribed to chat {ChatId}", userId, chatId);
    }

    public async Task UnsubscribeFromChat(long chatId)
    {
        var userId = GetUserId();
        if (userId == null) return;

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat:{chatId}");
        await _redisService.UnsubscribeFromChatAsync(userId.Value, chatId, Context.ConnectionId);

        _logger.LogDebug("User {UserId} unsubscribed from chat {ChatId}", userId, chatId);
    }

    public async Task MarkNotificationAsRead(long notificationId)
    {
        var userId = GetUserId();
        if (userId == null) return;

        await _notificationService.MarkAsReadAsync(notificationId, userId.Value);
    }

    public async Task MarkAllNotificationsAsRead()
    {
        var userId = GetUserId();
        if (userId == null) return;

        await _notificationService.MarkAllAsReadAsync(userId.Value);
    }

    public async Task UpdateStatus(string status)
    {
        var userId = GetUserId();
        if (userId == null) return;

        // Valid statuses: online, away, busy, offline
        var validStatuses = new[] { "online", "away", "busy", "offline" };
        if (!validStatuses.Contains(status.ToLowerInvariant()))
        {
            return;
        }

        await _redisService.UpdateUserStatusAsync(userId.Value, status.ToLowerInvariant());
        
        // Notify contacts of status change
        await NotifyContactsOfStatusAsync(userId.Value, status.ToLowerInvariant() != "offline");
    }

    public async Task Heartbeat()
    {
        var userId = GetUserId();
        if (userId == null) return;

        await _redisService.UpdateLastHeartbeatAsync(userId.Value, Context.ConnectionId);
    }

    private async Task NotifyContactsOfStatusAsync(long userId, bool isOnline)
    {
        // Get user's contacts (chat members where user is a member)
        var contactUserIds = await _redisService.GetContactUserIdsAsync(userId);
        
        foreach (var contactId in contactUserIds)
        {
            var connections = await _redisService.GetUserConnectionsAsync(contactId);
            foreach (var connectionId in connections)
            {
                await Clients.Client(connectionId).SendAsync("UserStatusChanged", new
                {
                    UserId = userId,
                    IsOnline = isOnline,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
    }

    private long? GetUserId()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !long.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        return userId;
    }
}
