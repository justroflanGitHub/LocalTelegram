using MessageService.Data;
using MessageService.Models;
using Microsoft.EntityFrameworkCore;

namespace MessageService.Services;

public interface IChatService
{
    Task<Chat?> CreateChatAsync(long ownerId, CreateChatRequest request);
    Task<Chat?> GetChatAsync(long chatId, long userId);
    Task<List<ChatDto>> GetUserChatsAsync(long userId);
    Task<Chat?> UpdateChatAsync(long chatId, long userId, UpdateChatRequest request);
    Task<bool> DeleteChatAsync(long chatId, long userId);
    Task<bool> AddMemberAsync(long chatId, long userId, long addedById);
    Task<bool> RemoveMemberAsync(long chatId, long userId, long removedById);
    Task<bool> UpdateMemberRoleAsync(long chatId, long userId, MemberRole newRole, long updatedById);
    Task<List<ChatMember>> GetChatMembersAsync(long chatId, long requestingUserId);
    Task<Chat?> GetOrCreatePrivateChatAsync(long userId1, long userId2);
}

public class ChatService : IChatService
{
    private readonly MessageDbContext _context;
    private readonly ILogger<ChatService> _logger;

    public ChatService(MessageDbContext context, ILogger<ChatService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Chat?> CreateChatAsync(long ownerId, CreateChatRequest request)
    {
        var chat = new Chat
        {
            Type = request.Type,
            Title = request.Title,
            Description = request.Description,
            OwnerId = ownerId
        };

        _context.Chats.Add(chat);
        await _context.SaveChangesAsync();

        // Add owner as member
        _context.ChatMembers.Add(new ChatMember
        {
            ChatId = chat.Id,
            UserId = ownerId,
            Role = MemberRole.Owner
        });

        // Add other members
        foreach (var memberId in request.MemberIds.Where(id => id != ownerId))
        {
            _context.ChatMembers.Add(new ChatMember
            {
                ChatId = chat.Id,
                UserId = memberId,
                Role = MemberRole.Member
            });
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Chat {ChatId} created by user {UserId}", chat.Id, ownerId);

        return chat;
    }

    public async Task<Chat?> GetChatAsync(long chatId, long userId)
    {
        var isMember = await _context.ChatMembers
            .AnyAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

        if (!isMember) return null;

        return await _context.Chats.FindAsync(chatId);
    }

    public async Task<List<ChatDto>> GetUserChatsAsync(long userId)
    {
        var chatMemberships = await _context.ChatMembers
            .Where(cm => cm.UserId == userId)
            .Select(cm => cm.ChatId)
            .ToListAsync();

        var chats = await _context.Chats
            .Where(c => chatMemberships.Contains(c.Id))
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();

        var result = new List<ChatDto>();

        foreach (var chat in chats)
        {
            var memberCount = await _context.ChatMembers
                .CountAsync(cm => cm.ChatId == chat.Id);

            var lastMessage = await _context.Messages
                .Where(m => m.ChatId == chat.Id && !m.IsDeleted)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync();

            var unreadCount = await GetUnreadCountForChatAsync(chat.Id, userId);

            result.Add(ChatDto.FromChat(chat, memberCount, 
                lastMessage != null ? MessageDto.FromMessage(lastMessage) : null, 
                unreadCount));
        }

        return result;
    }

    public async Task<Chat?> UpdateChatAsync(long chatId, long userId, UpdateChatRequest request)
    {
        var chat = await _context.Chats.FindAsync(chatId);
        if (chat == null) return null;

        var member = await _context.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

        if (member == null || (member.Role != MemberRole.Owner && member.Role != MemberRole.Admin))
        {
            return null;
        }

        if (request.Title != null) chat.Title = request.Title;
        if (request.Description != null) chat.Description = request.Description;
        chat.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return chat;
    }

    public async Task<bool> DeleteChatAsync(long chatId, long userId)
    {
        var chat = await _context.Chats.FindAsync(chatId);
        if (chat == null) return false;

        if (chat.OwnerId != userId)
        {
            return false;
        }

        _context.Chats.Remove(chat);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Chat {ChatId} deleted by user {UserId}", chatId, userId);

        return true;
    }

    public async Task<bool> AddMemberAsync(long chatId, long userId, long addedById)
    {
        var chat = await _context.Chats.FindAsync(chatId);
        if (chat == null) return false;

        if (chat.Type == ChatType.Private)
        {
            return false; // Cannot add members to private chat
        }

        var adderMember = await _context.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == addedById);

        if (adderMember == null || (adderMember.Role != MemberRole.Owner && adderMember.Role != MemberRole.Admin))
        {
            return false;
        }

        var existingMember = await _context.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

        if (existingMember != null)
        {
            return true; // Already a member
        }

        _context.ChatMembers.Add(new ChatMember
        {
            ChatId = chatId,
            UserId = userId,
            Role = MemberRole.Member
        });

        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} added to chat {ChatId} by {AddedById}", userId, chatId, addedById);

        return true;
    }

    public async Task<bool> RemoveMemberAsync(long chatId, long userId, long removedById)
    {
        var member = await _context.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

        if (member == null) return false;

        // User can remove themselves, or admin/owner can remove others
        if (userId != removedById)
        {
            var removerMember = await _context.ChatMembers
                .FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == removedById);

            if (removerMember == null || (removerMember.Role != MemberRole.Owner && removerMember.Role != MemberRole.Admin))
            {
                return false;
            }

            // Cannot remove owner
            if (member.Role == MemberRole.Owner)
            {
                return false;
            }
        }

        _context.ChatMembers.Remove(member);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} removed from chat {ChatId} by {RemovedById}", userId, chatId, removedById);

        return true;
    }

    public async Task<bool> UpdateMemberRoleAsync(long chatId, long userId, MemberRole newRole, long updatedById)
    {
        if (newRole == MemberRole.Owner)
        {
            return false; // Cannot set owner role this way
        }

        var updaterMember = await _context.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == updatedById);

        if (updaterMember == null || updaterMember.Role != MemberRole.Owner)
        {
            return false; // Only owner can change roles
        }

        var member = await _context.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

        if (member == null || member.Role == MemberRole.Owner)
        {
            return false; // Cannot change owner's role
        }

        member.Role = newRole;
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<List<ChatMember>> GetChatMembersAsync(long chatId, long requestingUserId)
    {
        var isMember = await _context.ChatMembers
            .AnyAsync(cm => cm.ChatId == chatId && cm.UserId == requestingUserId);

        if (!isMember) return new List<ChatMember>();

        return await _context.ChatMembers
            .Where(cm => cm.ChatId == chatId)
            .ToListAsync();
    }

    public async Task<Chat?> GetOrCreatePrivateChatAsync(long userId1, long userId2)
    {
        // Find existing private chat between users
        var existingChat = await (from c in _context.Chats
                                  join cm1 in _context.ChatMembers on c.Id equals cm1.ChatId
                                  join cm2 in _context.ChatMembers on c.Id equals cm2.ChatId
                                  where c.Type == ChatType.Private
                                        && cm1.UserId == userId1
                                        && cm2.UserId == userId2
                                  select c).FirstOrDefaultAsync();

        if (existingChat != null)
        {
            return existingChat;
        }

        // Create new private chat
        var chat = new Chat
        {
            Type = ChatType.Private,
            OwnerId = userId1
        };

        _context.Chats.Add(chat);
        await _context.SaveChangesAsync();

        _context.ChatMembers.AddRange(
            new ChatMember { ChatId = chat.Id, UserId = userId1, Role = MemberRole.Member },
            new ChatMember { ChatId = chat.Id, UserId = userId2, Role = MemberRole.Member }
        );

        await _context.SaveChangesAsync();

        _logger.LogInformation("Private chat {ChatId} created between {UserId1} and {UserId2}", 
            chat.Id, userId1, userId2);

        return chat;
    }

    private async Task<int> GetUnreadCountForChatAsync(long chatId, long userId)
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
