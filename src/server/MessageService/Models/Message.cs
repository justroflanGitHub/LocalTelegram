namespace MessageService.Models;

public enum ChatType
{
    Private,
    Group,
    Channel
}

public enum MemberRole
{
    Owner,
    Admin,
    Moderator,
    Member
}

public enum MessageStatus
{
    Sending,
    Sent,
    Delivered,
    Read,
    Failed
}

public class Chat
{
    public long Id { get; set; }
    public ChatType Type { get; set; } = ChatType.Private;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public long? AvatarId { get; set; }
    public long? OwnerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Settings { get; set; } = new();
}

public class ChatMember
{
    public long ChatId { get; set; }
    public long UserId { get; set; }
    public MemberRole Role { get; set; } = MemberRole.Member;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public long? LastReadMessageId { get; set; }
    public DateTime? MutedUntil { get; set; }
}

public class Message
{
    public long Id { get; set; }
    public long ChatId { get; set; }
    public long SenderId { get; set; }
    public long? ReplyToId { get; set; }
    public long? ForwardFromId { get; set; }
    public string? Content { get; set; }
    public string ContentType { get; set; } = "text";
    public long? MediaId { get; set; }
    public MessageStatus Status { get; set; } = MessageStatus.Sending;
    public bool IsEdited { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class MessageStatusLog
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public long UserId { get; set; }
    public MessageStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// DTOs
public class SendMessageRequest
{
    public long ChatId { get; set; }
    public string? Content { get; set; }
    public string ContentType { get; set; } = "text";
    public long? ReplyToId { get; set; }
    public long? ForwardFromId { get; set; }
    public long? MediaId { get; set; }
}

public class MessageDto
{
    public long Id { get; set; }
    public long ChatId { get; set; }
    public long SenderId { get; set; }
    public long? ReplyToId { get; set; }
    public long? ForwardFromId { get; set; }
    public string? Content { get; set; }
    public string ContentType { get; set; } = "text";
    public long? MediaId { get; set; }
    public MessageStatus Status { get; set; }
    public bool IsEdited { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static MessageDto FromMessage(Message message)
    {
        return new MessageDto
        {
            Id = message.Id,
            ChatId = message.ChatId,
            SenderId = message.SenderId,
            ReplyToId = message.ReplyToId,
            ForwardFromId = message.ForwardFromId,
            Content = message.IsDeleted ? null : message.Content,
            ContentType = message.ContentType,
            MediaId = message.MediaId,
            Status = message.Status,
            IsEdited = message.IsEdited,
            IsDeleted = message.IsDeleted,
            CreatedAt = message.CreatedAt,
            UpdatedAt = message.UpdatedAt
        };
    }
}

public class ChatDto
{
    public long Id { get; set; }
    public ChatType Type { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public long? AvatarId { get; set; }
    public long? OwnerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int MemberCount { get; set; }
    public MessageDto? LastMessage { get; set; }
    public int UnreadCount { get; set; }

    public static ChatDto FromChat(Chat chat, int memberCount = 0, MessageDto? lastMessage = null, int unreadCount = 0)
    {
        return new ChatDto
        {
            Id = chat.Id,
            Type = chat.Type,
            Title = chat.Title,
            Description = chat.Description,
            AvatarId = chat.AvatarId,
            OwnerId = chat.OwnerId,
            CreatedAt = chat.CreatedAt,
            UpdatedAt = chat.UpdatedAt,
            MemberCount = memberCount,
            LastMessage = lastMessage,
            UnreadCount = unreadCount
        };
    }
}

public class CreateChatRequest
{
    public ChatType Type { get; set; } = ChatType.Private;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public List<long> MemberIds { get; set; } = new();
}

public class UpdateChatRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
}

public class AddMemberRequest
{
    public long UserId { get; set; }
    public MemberRole Role { get; set; } = MemberRole.Member;
}

public class GetMessagesRequest
{
    public long ChatId { get; set; }
    public long? BeforeId { get; set; }
    public long? AfterId { get; set; }
    public int Limit { get; set; } = 50;
}

public class EditMessageRequest
{
    public string Content { get; set; } = string.Empty;
}
