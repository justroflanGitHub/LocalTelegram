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
    
    // Extended features
    Task<Message?> ReplyToMessageAsync(long senderId, long chatId, long replyToId, string content);
    Task<Message?> ForwardMessageAsync(long senderId, long messageId, long targetChatId);
    Task<List<Message>> ForwardMultipleMessagesAsync(long senderId, List<long> messageIds, long targetChatId);
    Task<List<MessageDto>> SearchMessagesAsync(long chatId, long userId, string query, int limit);
    Task<bool> PinMessageAsync(long messageId, long userId);
    Task<bool> UnpinMessageAsync(long messageId, long userId);
    Task<List<MessageDto>> GetPinnedMessagesAsync(long chatId, long userId);
    Task<MessageReaction?> AddReactionAsync(long messageId, long userId, string emoji);
    Task<bool> RemoveReactionAsync(long messageId, long userId, string emoji);
    Task<List<MessageReactionDto>> GetReactionsAsync(long messageId);
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

    #region Extended Features

    /// <summary>
    /// Reply to a message
    /// </summary>
    public async Task<Message?> ReplyToMessageAsync(long senderId, long chatId, long replyToId, string content)
    {
        // Verify the message being replied to exists and is in the same chat
        var replyToMessage = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == replyToId && !m.IsDeleted);
        
        if (replyToMessage == null)
        {
            _logger.LogWarning("Cannot reply to message {MessageId}: not found", replyToId);
            return null;
        }

        // Verify user is member of chat
        var isMember = await _context.ChatMembers
            .AnyAsync(cm => cm.ChatId == chatId && cm.UserId == senderId);

        if (!isMember)
        {
            _logger.LogWarning("User {UserId} is not a member of chat {ChatId}", senderId, chatId);
            return null;
        }

        var message = new Message
        {
            ChatId = chatId,
            SenderId = senderId,
            Content = content,
            ContentType = "text",
            ReplyToId = replyToId,
            Status = MessageStatus.Sent
        };

        _context.Messages.Add(message);
        
        // Update chat's UpdatedAt
        var chat = await _context.Chats.FindAsync(chatId);
        if (chat != null)
        {
            chat.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        // Publish reply event
        await _rabbitMqService.PublishMessageEventAsync("message.reply", new
        {
            MessageId = message.Id,
            ChatId = message.ChatId,
            SenderId = message.SenderId,
            ReplyToId = replyToId,
            OriginalSenderId = replyToMessage.SenderId
        });

        _logger.LogInformation("Message {MessageId} is a reply to {ReplyToId}", message.Id, replyToId);

        return message;
    }

    /// <summary>
    /// Forward a message to another chat
    /// </summary>
    public async Task<Message?> ForwardMessageAsync(long senderId, long messageId, long targetChatId)
    {
        // Get original message
        var originalMessage = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && !m.IsDeleted);
        
        if (originalMessage == null)
        {
            _logger.LogWarning("Cannot forward message {MessageId}: not found", messageId);
            return null;
        }

        // Verify user is member of target chat
        var isMember = await _context.ChatMembers
            .AnyAsync(cm => cm.ChatId == targetChatId && cm.UserId == senderId);

        if (!isMember)
        {
            _logger.LogWarning("User {UserId} is not a member of target chat {ChatId}", senderId, targetChatId);
            return null;
        }

        var forwardedMessage = new Message
        {
            ChatId = targetChatId,
            SenderId = senderId,
            Content = originalMessage.Content,
            ContentType = originalMessage.ContentType,
            ForwardFromId = messageId,
            MediaId = originalMessage.MediaId,
            Status = MessageStatus.Sent,
            Metadata = new Dictionary<string, object>
            {
                ["forwarded_from_chat"] = originalMessage.ChatId,
                ["forwarded_from_user"] = originalMessage.SenderId,
                ["forwarded_at"] = DateTime.UtcNow.ToString("O"),
                ["original_created_at"] = originalMessage.CreatedAt.ToString("O")
            }
        };

        _context.Messages.Add(forwardedMessage);
        
        // Update target chat's UpdatedAt
        var chat = await _context.Chats.FindAsync(targetChatId);
        if (chat != null)
        {
            chat.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        // Publish forward event
        await _rabbitMqService.PublishMessageEventAsync("message.forward", new
        {
            MessageId = forwardedMessage.Id,
            ChatId = forwardedMessage.ChatId,
            SenderId = forwardedMessage.SenderId,
            OriginalMessageId = messageId,
            OriginalChatId = originalMessage.ChatId
        });

        _logger.LogInformation("Message {OriginalId} forwarded to chat {TargetChatId} as {NewId}", 
            messageId, targetChatId, forwardedMessage.Id);

        return forwardedMessage;
    }

    /// <summary>
    /// Forward multiple messages to another chat
    /// </summary>
    public async Task<List<Message>> ForwardMultipleMessagesAsync(long senderId, List<long> messageIds, long targetChatId)
    {
        var forwardedMessages = new List<Message>();

        // Verify user is member of target chat
        var isMember = await _context.ChatMembers
            .AnyAsync(cm => cm.ChatId == targetChatId && cm.UserId == senderId);

        if (!isMember)
        {
            _logger.LogWarning("User {UserId} is not a member of target chat {ChatId}", senderId, targetChatId);
            return forwardedMessages;
        }

        // Get original messages in order
        var originalMessages = await _context.Messages
            .Where(m => messageIds.Contains(m.Id) && !m.IsDeleted)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        foreach (var originalMessage in originalMessages)
        {
            var forwardedMessage = new Message
            {
                ChatId = targetChatId,
                SenderId = senderId,
                Content = originalMessage.Content,
                ContentType = originalMessage.ContentType,
                ForwardFromId = originalMessage.Id,
                MediaId = originalMessage.MediaId,
                Status = MessageStatus.Sent,
                Metadata = new Dictionary<string, object>
                {
                    ["forwarded_from_chat"] = originalMessage.ChatId,
                    ["forwarded_from_user"] = originalMessage.SenderId,
                    ["forwarded_at"] = DateTime.UtcNow.ToString("O"),
                    ["original_created_at"] = originalMessage.CreatedAt.ToString("O")
                }
            };

            _context.Messages.Add(forwardedMessage);
            forwardedMessages.Add(forwardedMessage);
        }

        if (forwardedMessages.Any())
        {
            // Update target chat's UpdatedAt
            var chat = await _context.Chats.FindAsync(targetChatId);
            if (chat != null)
            {
                chat.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            // Publish bulk forward event
            await _rabbitMqService.PublishMessageEventAsync("message.forward_bulk", new
            {
                TargetChatId = targetChatId,
                SenderId = senderId,
                MessageCount = forwardedMessages.Count,
                MessageIds = forwardedMessages.Select(m => m.Id).ToList()
            });

            _logger.LogInformation("Forwarded {Count} messages to chat {TargetChatId}", 
                forwardedMessages.Count, targetChatId);
        }

        return forwardedMessages;
    }

    /// <summary>
    /// Search messages in a chat
    /// </summary>
    public async Task<List<MessageDto>> SearchMessagesAsync(long chatId, long userId, string query, int limit)
    {
        // Verify user is member of chat
        var isMember = await _context.ChatMembers
            .AnyAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

        if (!isMember)
        {
            return new List<MessageDto>();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<MessageDto>();
        }

        var messages = await _context.Messages
            .Where(m => m.ChatId == chatId && 
                       !m.IsDeleted && 
                       m.Content != null &&
                       m.Content.ToLower().Contains(query.ToLower()))
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync();

        return messages.Select(MessageDto.FromMessage).ToList();
    }

    /// <summary>
    /// Pin a message in a chat
    /// </summary>
    public async Task<bool> PinMessageAsync(long messageId, long userId)
    {
        var message = await _context.Messages.FindAsync(messageId);
        if (message == null || message.IsDeleted)
        {
            return false;
        }

        // Check if user has permission (admin or owner)
        var memberRole = await _context.ChatMembers
            .Where(cm => cm.ChatId == message.ChatId && cm.UserId == userId)
            .Select(cm => cm.Role)
            .FirstOrDefaultAsync();

        if (memberRole != MemberRole.Admin && memberRole != MemberRole.Owner)
        {
            _logger.LogWarning("User {UserId} does not have permission to pin messages in chat {ChatId}", 
                userId, message.ChatId);
            return false;
        }

        message.Metadata["pinned"] = true;
        message.Metadata["pinned_at"] = DateTime.UtcNow.ToString("O");
        message.Metadata["pinned_by"] = userId;
        message.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Publish pin event
        await _rabbitMqService.PublishMessageEventAsync("message.pinned", new
        {
            MessageId = message.Id,
            ChatId = message.ChatId,
            PinnedBy = userId
        });

        _logger.LogInformation("Message {MessageId} pinned in chat {ChatId}", messageId, message.ChatId);

        return true;
    }

    /// <summary>
    /// Unpin a message
    /// </summary>
    public async Task<bool> UnpinMessageAsync(long messageId, long userId)
    {
        var message = await _context.Messages.FindAsync(messageId);
        if (message == null)
        {
            return false;
        }

        // Check if user has permission
        var memberRole = await _context.ChatMembers
            .Where(cm => cm.ChatId == message.ChatId && cm.UserId == userId)
            .Select(cm => cm.Role)
            .FirstOrDefaultAsync();

        if (memberRole != MemberRole.Admin && memberRole != MemberRole.Owner)
        {
            return false;
        }

        if (message.Metadata.ContainsKey("pinned"))
        {
            message.Metadata.Remove("pinned");
            message.Metadata.Remove("pinned_at");
            message.Metadata.Remove("pinned_by");
            message.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Publish unpin event
            await _rabbitMqService.PublishMessageEventAsync("message.unpinned", new
            {
                MessageId = message.Id,
                ChatId = message.ChatId,
                UnpinnedBy = userId
            });

            _logger.LogInformation("Message {MessageId} unpinned in chat {ChatId}", messageId, message.ChatId);
        }

        return true;
    }

    /// <summary>
    /// Get all pinned messages in a chat
    /// </summary>
    public async Task<List<MessageDto>> GetPinnedMessagesAsync(long chatId, long userId)
    {
        // Verify user is member of chat
        var isMember = await _context.ChatMembers
            .AnyAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

        if (!isMember)
        {
            return new List<MessageDto>();
        }

        var messages = await _context.Messages
            .Where(m => m.ChatId == chatId && 
                       !m.IsDeleted && 
                       m.Metadata.ContainsKey("pinned") &&
                       (bool)m.Metadata["pinned"] == true)
            .OrderByDescending(m => m.Metadata.ContainsKey("pinned_at") ? m.Metadata["pinned_at"] : m.CreatedAt)
            .ToListAsync();

        return messages.Select(MessageDto.FromMessage).ToList();
    }

    /// <summary>
    /// Add a reaction to a message
    /// </summary>
    public async Task<MessageReaction?> AddReactionAsync(long messageId, long userId, string emoji)
    {
        var message = await _context.Messages.FindAsync(messageId);
        if (message == null || message.IsDeleted)
        {
            return null;
        }

        // Verify user is member of chat
        var isMember = await _context.ChatMembers
            .AnyAsync(cm => cm.ChatId == message.ChatId && cm.UserId == userId);

        if (!isMember)
        {
            return null;
        }

        // Check if reaction already exists
        var existingReaction = await _context.MessageReactions
            .FirstOrDefaultAsync(r => r.MessageId == messageId && r.UserId == userId && r.Emoji == emoji);

        if (existingReaction != null)
        {
            return existingReaction; // Already reacted
        }

        var reaction = new MessageReaction
        {
            MessageId = messageId,
            UserId = userId,
            Emoji = emoji,
            CreatedAt = DateTime.UtcNow
        };

        _context.MessageReactions.Add(reaction);
        await _context.SaveChangesAsync();

        // Publish reaction event
        await _rabbitMqService.PublishMessageEventAsync("message.reaction_added", new
        {
            MessageId = messageId,
            ChatId = message.ChatId,
            UserId = userId,
            Emoji = emoji
        });

        _logger.LogInformation("Reaction {Emoji} added to message {MessageId} by user {UserId}", 
            emoji, messageId, userId);

        return reaction;
    }

    /// <summary>
    /// Remove a reaction from a message
    /// </summary>
    public async Task<bool> RemoveReactionAsync(long messageId, long userId, string emoji)
    {
        var reaction = await _context.MessageReactions
            .FirstOrDefaultAsync(r => r.MessageId == messageId && r.UserId == userId && r.Emoji == emoji);

        if (reaction == null)
        {
            return false;
        }

        _context.MessageReactions.Remove(reaction);
        await _context.SaveChangesAsync();

        // Publish reaction removed event
        await _rabbitMqService.PublishMessageEventAsync("message.reaction_removed", new
        {
            MessageId = messageId,
            UserId = userId,
            Emoji = emoji
        });

        _logger.LogInformation("Reaction {Emoji} removed from message {MessageId} by user {UserId}", 
            emoji, messageId, userId);

        return true;
    }

    /// <summary>
    /// Get all reactions for a message
    /// </summary>
    public async Task<List<MessageReactionDto>> GetReactionsAsync(long messageId)
    {
        var reactions = await _context.MessageReactions
            .Where(r => r.MessageId == messageId)
            .GroupBy(r => r.Emoji)
            .Select(g => new MessageReactionDto
            {
                Emoji = g.Key,
                Count = g.Count(),
                UserIds = g.Select(r => r.UserId).ToList()
            })
            .ToListAsync();

        return reactions;
    }

    #endregion
}
