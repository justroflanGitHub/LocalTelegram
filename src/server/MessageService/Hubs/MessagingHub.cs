using MessageService.Models;
using MessageService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace MessageService.Hubs;

[Authorize]
public class MessagingHub : Hub
{
    private readonly IMessageService _messageService;
    private readonly IChatService _chatService;
    private readonly IRedisService _redisService;
    private readonly ILogger<MessagingHub> _logger;

    public MessagingHub(
        IMessageService messageService,
        IChatService chatService,
        IRedisService redisService,
        ILogger<MessagingHub> logger)
    {
        _messageService = messageService;
        _chatService = chatService;
        _redisService = redisService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        if (userId == null)
        {
            Context.Abort();
            return;
        }

        // Track user connection
        await _redisService.SetUserOnlineAsync(userId.Value, Context.ConnectionId);

        _logger.LogInformation("User {UserId} connected with connection {ConnectionId}", 
            userId, Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        if (userId != null)
        {
            await _redisService.SetUserOfflineAsync(userId.Value, Context.ConnectionId);

            _logger.LogInformation("User {UserId} disconnected", userId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinChat(long chatId)
    {
        var userId = GetUserId();
        if (userId == null) return;

        // Verify user is a member of the chat
        var chat = await _chatService.GetChatAsync(chatId, userId.Value);
        if (chat == null)
        {
            _logger.LogWarning("User {UserId} attempted to join chat {ChatId} they are not a member of", 
                userId, chatId);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat:{chatId}");
        await _redisService.SubscribeToChatAsync(chatId, Context.ConnectionId);

        _logger.LogDebug("User {UserId} joined chat {ChatId}", userId, chatId);

        // Notify others in the chat
        await Clients.OthersInGroup($"chat:{chatId}").SendAsync("UserJoined", new
        {
            ChatId = chatId,
            UserId = userId
        });
    }

    public async Task LeaveChat(long chatId)
    {
        var userId = GetUserId();
        if (userId == null) return;

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat:{chatId}");
        await _redisService.UnsubscribeFromChatAsync(chatId, Context.ConnectionId);

        _logger.LogDebug("User {UserId} left chat {ChatId}", userId, chatId);
    }

    public async Task SendMessage(SendMessageRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return;

        var message = await _messageService.SendMessageAsync(userId.Value, request);
        if (message == null)
        {
            await Clients.Caller.SendAsync("Error", "Failed to send message");
            return;
        }

        var messageDto = MessageDto.FromMessage(message);

        // Broadcast to all chat members
        await Clients.Group($"chat:{request.ChatId}").SendAsync("MessageReceived", messageDto);
    }

    public async Task MarkAsRead(long messageId)
    {
        var userId = GetUserId();
        if (userId == null) return;

        var success = await _messageService.MarkAsReadAsync(messageId, userId.Value);
        if (success)
        {
            var message = await _messageService.GetMessageAsync(messageId);
            if (message != null)
            {
                // Notify sender that message was read
                await Clients.Group($"chat:{message.ChatId}").SendAsync("MessageRead", new
                {
                    MessageId = messageId,
                    UserId = userId,
                    ChatId = message.ChatId
                });
            }
        }
    }

    public async Task TypingStart(long chatId)
    {
        var userId = GetUserId();
        if (userId == null) return;

        await Clients.OthersInGroup($"chat:{chatId}").SendAsync("UserTyping", new
        {
            ChatId = chatId,
            UserId = userId
        });
    }

    public async Task TypingStop(long chatId)
    {
        var userId = GetUserId();
        if (userId == null) return;

        await Clients.OthersInGroup($"chat:{chatId}").SendAsync("UserStoppedTyping", new
        {
            ChatId = chatId,
            UserId = userId
        });
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
