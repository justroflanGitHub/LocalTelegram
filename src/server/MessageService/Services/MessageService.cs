using MessageService.Data;
using MessageService.Models;
using Microsoft.EntityFrameworkCore;

namespace MessageService.Services;

public interface IMessageService
{
    Task<Message?> SendMessageAsync(long senderId, SendMessageRequest request);
    Task<Message?> GetMessageAsync(long messageId);
    Task<List<MessageDto>> GetMessagesAsync(long chatId, long userId, long? beforeId, long? afterId, int limit);
    Task<Message?> EditMessageAsync(long messageId, long userId, string newContent);
    Task<bool> DeleteMessageAsync(long messageId, long userId);
    Task<bool> MarkAsReadAsync(long messageId, long userId);
    Task<int> GetUnreadCountAsync(long chatId, long userId);
}

public class MessageService : IMessageService
{
    private readonly MessageDbContext _context;
    private readonly IRedisService _redisService;
    private readonly IRabbitMqService _rabbitMqService;
    private readonly ILogger<MessageService> _logger;

    public MessageService(
        MessageDbContext context,
        IRedisService redisService,
        IRabbitMqService rabbitMqService,
        ILogger<MessageService> logger)
    {
        _context = context;
        _redisService = redisService;
        _rabbitMqService = rabbitMqService;
        _logger = logger;
    }

    public async Task<Message?> SendMessageAsync(long senderId, SendMessageRequest request)
    {
        // Verify user is member of chat
        var isMember = await _context.ChatMembers
            .AnyAsync(cm => cm.ChatId == request.ChatId && cm.UserId == senderId);

        if (!isMember)
        {
            _logger.LogWarning("User {UserId} is not a member of chat {ChatId}", senderId, request.ChatId);
            return null;
        }

        var message = new Message
        {
            ChatId = request.ChatId,
            SenderId = senderId,
            Content = request.Content,
            ContentType = request.ContentType,
            ReplyToId = request.ReplyToId,
            ForwardFromId = request.ForwardFromId,
            MediaId = request.MediaId,
            Status = MessageStatus.Sent
        };

        _context.Messages.Add(message);
        
        // Update chat's UpdatedAt
        var chat = await _context.Chats.FindAsync(request.ChatId);
        if (chat != null)
        {
            chat.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        // Publish message event
        await _rabbitMqService.PublishMessageEventAsync("message.created", new
        {
            MessageId = message.Id,
            ChatId = message.ChatId,
            SenderId = message.SenderId,
            CreatedAt = message.CreatedAt
        });

        // Cache recent message
        await _redisService.CacheRecentMessageAsync(message.ChatId, MessageDto.FromMessage(message));

        _logger.LogInformation("Message {MessageId} created in chat {ChatId}", message.Id, message.ChatId);

        return message;
    }

    public async Task<Message?> GetMessageAsync(long messageId)
    {
        return await _context.Messages.FindAsync(messageId);
    }

    public async Task<List<MessageDto>> GetMessagesAsync(long chatId, long userId, long? beforeId, long? afterId, int limit)
    {
        // Verify user is member of chat
        var isMember = await _context.ChatMembers
            .AnyAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

        if (!isMember)
        {
            return new List<MessageDto>();
        }

        var query = _context.Messages
            .Where(m => m.ChatId == chatId && !m.IsDeleted);

        if (beforeId.HasValue)
        {
            query = query.Where(m => m.Id < beforeId.Value);
        }

        if (afterId.HasValue)
        {
            query = query.Where(m => m.Id > afterId.Value);
        }

        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync();

        return messages.Select(MessageDto.FromMessage).Reverse().ToList();
    }

    public async Task<Message?> EditMessageAsync(long messageId, long userId, string newContent)
    {
        var message = await _context.Messages.FindAsync(messageId);
        if (message == null) return null;

        if (message.SenderId != userId)
        {
            _logger.LogWarning("User {UserId} attempted to edit message {MessageId} owned by {SenderId}", 
                userId, messageId, message.SenderId);
            return null;
        }

        message.Content = newContent;
        message.IsEdited = true;
        message.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Publish edit event
        await _rabbitMqService.PublishMessageEventAsync("message.edited", new
        {
            MessageId = message.Id,
            ChatId = message.ChatId,
            SenderId = message.SenderId
        });

        return message;
    }

    public async Task<bool> DeleteMessageAsync(long messageId, long userId)
    {
        var message = await _context.Messages.FindAsync(messageId);
        if (message == null) return false;

        // Only sender or chat admin can delete
        if (message.SenderId != userId)
        {
            var memberRole = await _context.ChatMembers
                .Where(cm => cm.ChatId == message.ChatId && cm.UserId == userId)
                .Select(cm => cm.Role)
                .FirstOrDefaultAsync();

            if (memberRole != MemberRole.Admin && memberRole != MemberRole.Owner)
            {
                return false;
            }
        }

        message.IsDeleted = true;
        message.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Publish delete event
        await _rabbitMqService.PublishMessageEventAsync("message.deleted", new
        {
            MessageId = message.Id,
            ChatId = message.ChatId
        });

        return true;
    }

    public async Task<bool> MarkAsReadAsync(long messageId, long userId)
    {
        var message = await _context.Messages.FindAsync(messageId);
        if (message == null) return false;

        // Check if already read
        var existingLog = await _context.MessageStatusLogs
            .FirstOrDefaultAsync(l => l.MessageId == messageId && l.UserId == userId);

        if (existingLog != null)
        {
            return true; // Already read
        }

        // Add read status
        _context.MessageStatusLogs.Add(new MessageStatusLog
        {
            MessageId = messageId,
            UserId = userId,
            Status = MessageStatus.Read
        });

        // Update last read for chat member
        var chatMember = await _context.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == message.ChatId && cm.UserId == userId);

        if (chatMember != null && (!chatMember.LastReadMessageId.HasValue || chatMember.LastReadMessageId < messageId))
        {
            chatMember.LastReadMessageId = messageId;
        }

        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<int> GetUnreadCountAsync(long chatId, long userId)
    {
        var chatMember = await _context.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

        if (chatMember == null) return 0;

        if (!chatMember.LastReadMessageId.HasValue)
        {
            return await _context.Messages
                .Where(m => m.ChatId == chatId && !m.IsDeleted)
                .CountAsync();
        }

        return await _context.Messages
            .Where(m => m.ChatId == chatId && m.Id > chatMember.LastReadMessageId.Value && !m.IsDeleted)
            .CountAsync();
    }
}
