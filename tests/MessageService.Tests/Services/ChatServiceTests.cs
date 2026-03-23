using FluentAssertions;
using MessageService.Data;
using MessageService.Models;
using MessageService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MessageService.Tests.Services;

public class ChatServiceTests : IDisposable
{
    private readonly MessageDbContext _context;
    private readonly Mock<ILogger<ChatService>> _mockLogger;
    private readonly ChatService _chatService;

    public ChatServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<MessageDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new MessageDbContext(options);

        // Setup mocks
        _mockLogger = new Mock<ILogger<ChatService>>();

        // Create service instance
        _chatService = new ChatService(_context, _mockLogger.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    #region CreateChatAsync Tests

    [Fact]
    public async Task CreateChatAsync_WithValidData_ReturnsChat()
    {
        // Arrange
        var request = new CreateChatRequest
        {
            Type = ChatType.Group,
            Title = "Test Group",
            Description = "Test Description"
        };
        var ownerId = 1L;

        // Act
        var result = await _chatService.CreateChatAsync(ownerId, request);

        // Assert
        result.Should().NotBeNull();
        result!.Title.Should().Be("Test Group");
        result.Description.Should().Be("Test Description");
        result.Type.Should().Be(ChatType.Group);
        result.OwnerId.Should().Be(ownerId);
    }

    [Fact]
    public async Task CreateChatAsync_AddsOwnerAsMember()
    {
        // Arrange
        var request = new CreateChatRequest
        {
            Type = ChatType.Group,
            Title = "Test Group"
        };
        var ownerId = 1L;

        // Act
        var result = await _chatService.CreateChatAsync(ownerId, request);

        // Assert
        var ownerMember = await _context.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == result!.Id && cm.UserId == ownerId);
        ownerMember.Should().NotBeNull();
        ownerMember!.Role.Should().Be(MemberRole.Owner);
    }

    [Fact]
    public async Task CreateChatAsync_WithMemberIds_AddsAllMembers()
    {
        // Arrange
        var request = new CreateChatRequest
        {
            Type = ChatType.Group,
            Title = "Test Group",
            MemberIds = new List<long> { 1, 2, 3 }
        };
        var ownerId = 1L;

        // Act
        var result = await _chatService.CreateChatAsync(ownerId, request);

        // Assert
        var members = await _context.ChatMembers
            .Where(cm => cm.ChatId == result!.Id).ToListAsync();
        members.Should().HaveCount(3);
    }

    [Fact]
    public async Task CreateChatAsync_WithPrivateType_CreatesPrivateChat()
    {
        // Arrange
        var request = new CreateChatRequest
        {
            Type = ChatType.Private
        };
        var ownerId = 1L;

        // Act
        var result = await _chatService.CreateChatAsync(ownerId, request);

        // Assert
        result.Should().NotBeNull();
        result!.Type.Should().Be(ChatType.Private);
    }

    #endregion

    #region GetChatAsync Tests

    [Fact]
    public async Task GetChatAsync_WithMemberUser_ReturnsChat()
    {
        // Arrange
        var chat = new Chat
        {
            Type = ChatType.Group,
            Title = "Test Group",
            OwnerId = 1
        };
        _context.Chats.Add(chat);
        _context.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = 1, Role = MemberRole.Owner });
        await _context.SaveChangesAsync();

        // Act
        var result = await _chatService.GetChatAsync(chat.Id, 1);

        // Assert
        result.Should().NotBeNull();
        result!.Title.Should().Be("Test Group");
    }

    [Fact]
    public async Task GetChatAsync_WithNonMemberUser_ReturnsNull()
    {
        // Arrange
        var chat = new Chat
        {
            Type = ChatType.Group,
            Title = "Test Group",
            OwnerId = 1
        };
        _context.Chats.Add(chat);
        _context.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = 1, Role = MemberRole.Owner });
        await _context.SaveChangesAsync();

        // Act
        var result = await _chatService.GetChatAsync(chat.Id, 999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetChatAsync_WithNonExistentChat_ReturnsNull()
    {
        // Act
        var result = await _chatService.GetChatAsync(999, 1);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetUserChatsAsync Tests

    [Fact]
    public async Task GetUserChatsAsync_ReturnsUserChats()
    {
        // Arrange
        var userId = 1L;
        var chat1 = new Chat { Type = ChatType.Group, Title = "Group 1", OwnerId = userId };
        var chat2 = new Chat { Type = ChatType.Group, Title = "Group 2", OwnerId = userId };
        _context.Chats.AddRange(chat1, chat2);
        _context.ChatMembers.AddRange(
            new ChatMember { ChatId = chat1.Id, UserId = userId, Role = MemberRole.Owner },
            new ChatMember { ChatId = chat2.Id, UserId = userId, Role = MemberRole.Owner }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _chatService.GetUserChatsAsync(userId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(c => c.Title == "Group 1");
        result.Should().Contain(c => c.Title == "Group 2");
    }

    [Fact]
    public async Task GetUserChatsAsync_WithNoChats_ReturnsEmptyList()
    {
        // Arrange
        var userId = 999L;

        // Act
        var result = await _chatService.GetUserChatsAsync(userId);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region UpdateChatAsync Tests

    [Fact]
    public async Task UpdateChatAsync_WithOwner_UpdatesChat()
    {
        // Arrange
        var ownerId = 1L;
        var chat = new Chat { Type = ChatType.Group, Title = "Original Title", OwnerId = ownerId };
        _context.Chats.Add(chat);
        _context.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = ownerId, Role = MemberRole.Owner });
        await _context.SaveChangesAsync();

        var request = new UpdateChatRequest
        {
            Title = "Updated Title",
            Description = "Updated Description"
        };

        // Act
        var result = await _chatService.UpdateChatAsync(chat.Id, ownerId, request);

        // Assert
        result.Should().NotBeNull();
        result!.Title.Should().Be("Updated Title");
        result.Description.Should().Be("Updated Description");
    }

    [Fact]
    public async Task UpdateChatAsync_WithAdmin_UpdatesChat()
    {
        // Arrange
        var ownerId = 1L;
        var adminId = 2L;
        var chat = new Chat { Type = ChatType.Group, Title = "Original Title", OwnerId = ownerId };
        _context.Chats.Add(chat);
        _context.ChatMembers.AddRange(
            new ChatMember { ChatId = chat.Id, UserId = ownerId, Role = MemberRole.Owner },
            new ChatMember { ChatId = chat.Id, UserId = adminId, Role = MemberRole.Admin }
        );
        await _context.SaveChangesAsync();

        var request = new UpdateChatRequest
        {
            Title = "Updated Title"
        };

        // Act
        var result = await _chatService.UpdateChatAsync(chat.Id, adminId, request);

        // Assert
        result.Should().NotBeNull();
        result!.Title.Should().Be("Updated Title");
    }

    [Fact]
    public async Task UpdateChatAsync_WithRegularMember_ReturnsNull()
    {
        // Arrange
        var ownerId = 1L;
        var memberId = 2L;
        var chat = new Chat { Type = ChatType.Group, Title = "Original Title", OwnerId = ownerId };
        _context.Chats.Add(chat);
        _context.ChatMembers.AddRange(
            new ChatMember { ChatId = chat.Id, UserId = ownerId, Role = MemberRole.Owner },
            new ChatMember { ChatId = chat.Id, UserId = memberId, Role = MemberRole.Member }
        );
        await _context.SaveChangesAsync();

        var request = new UpdateChatRequest
        {
            Title = "Updated Title"
        };

        // Act
        var result = await _chatService.UpdateChatAsync(chat.Id, memberId, request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateChatAsync_WithNonMember_ReturnsNull()
    {
        // Arrange
        var chat = new Chat { Type = ChatType.Group, Title = "Original Title", OwnerId = 1 };
        _context.Chats.Add(chat);
        await _context.SaveChangesAsync();

        var request = new UpdateChatRequest
        {
            Title = "Updated Title"
        };

        // Act
        var result = await _chatService.UpdateChatAsync(chat.Id, 999, request);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region DeleteChatAsync Tests

    [Fact]
    public async Task DeleteChatAsync_WithOwner_DeletesChat()
    {
        // Arrange
        var userId = 1L;
        var chat = new Chat { Type = ChatType.Group, Title = "Test Group", OwnerId = userId };
        _context.Chats.Add(chat);
        await _context.SaveChangesAsync();

        // Act
        var result = await _chatService.DeleteChatAsync(chat.Id, userId);

        // Assert
        result.Should().BeTrue();
        var deletedChat = await _context.Chats.FindAsync(chat.Id);
        deletedChat.Should().BeNull();
    }

    [Fact]
    public async Task DeleteChatAsync_WithNonOwner_ReturnsFalse()
    {
        // Arrange
        var ownerId = 1L;
        var otherUserId = 2L;
        var chat = new Chat { Type = ChatType.Group, Title = "Test Group", OwnerId = ownerId };
        _context.Chats.Add(chat);
        await _context.SaveChangesAsync();

        // Act
        var result = await _chatService.DeleteChatAsync(chat.Id, otherUserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteChatAsync_WithNonExistentChat_ReturnsFalse()
    {
        // Act
        var result = await _chatService.DeleteChatAsync(999, 1);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region AddMemberAsync Tests

    [Fact]
    public async Task AddMemberAsync_WithAdmin_AddsMember()
    {
        // Arrange
        var adminId = 1L;
        var newMemberId = 2L;
        var chat = new Chat { Type = ChatType.Group, Title = "Test Group", OwnerId = adminId };
        _context.Chats.Add(chat);
        _context.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = adminId, Role = MemberRole.Admin });
        await _context.SaveChangesAsync();

        // Act
        var result = await _chatService.AddMemberAsync(chat.Id, newMemberId, adminId);

        // Assert
        result.Should().BeTrue();
        var member = await _context.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == chat.Id && cm.UserId == newMemberId);
        member.Should().NotBeNull();
    }

    [Fact]
    public async Task AddMemberAsync_ToPrivateChat_ReturnsFalse()
    {
        // Arrange
        var userId1 = 1L;
        var userId2 = 2L;
        var chat = new Chat { Type = ChatType.Private, OwnerId = userId1 };
        _context.Chats.Add(chat);
        _context.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = userId1, Role = MemberRole.Member });
        await _context.SaveChangesAsync();

        // Act
        var result = await _chatService.AddMemberAsync(chat.Id, userId2, userId1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AddMemberAsync_WithExistingMember_ReturnsTrue()
    {
        // Arrange
        var ownerId = 1L;
        var existingMemberId = 2L;
        var chat = new Chat { Type = ChatType.Group, Title = "Test Group", OwnerId = ownerId };
        _context.Chats.Add(chat);
        _context.ChatMembers.AddRange(
            new ChatMember { ChatId = chat.Id, UserId = ownerId, Role = MemberRole.Owner },
            new ChatMember { ChatId = chat.Id, UserId = existingMemberId, Role = MemberRole.Member }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _chatService.AddMemberAsync(chat.Id, existingMemberId, ownerId);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region RemoveMemberAsync Tests

    [Fact]
    public async Task RemoveMemberAsync_WithAdmin_RemovesMember()
    {
        // Arrange
        var adminId = 1L;
        var memberId = 2L;
        var chat = new Chat { Type = ChatType.Group, Title = "Test Group", OwnerId = adminId };
        _context.Chats.Add(chat);
        _context.ChatMembers.AddRange(
            new ChatMember { ChatId = chat.Id, UserId = adminId, Role = MemberRole.Admin },
            new ChatMember { ChatId = chat.Id, UserId = memberId, Role = MemberRole.Member }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _chatService.RemoveMemberAsync(chat.Id, memberId, adminId);

        // Assert
        result.Should().BeTrue();
        var member = await _context.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == chat.Id && cm.UserId == memberId);
        member.Should().BeNull();
    }

    [Fact]
    public async Task RemoveMemberAsync_SelfRemoval_ReturnsTrue()
    {
        // Arrange
        var memberId = 1L;
        var chat = new Chat { Type = ChatType.Group, Title = "Test Group", OwnerId = 999 };
        _context.Chats.Add(chat);
        _context.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = memberId, Role = MemberRole.Member });
        await _context.SaveChangesAsync();

        // Act
        var result = await _chatService.RemoveMemberAsync(chat.Id, memberId, memberId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveMemberAsync_CannotRemoveOwner_ReturnsFalse()
    {
        // Arrange
        var ownerId = 1L;
        var adminId = 2L;
        var chat = new Chat { Type = ChatType.Group, Title = "Test Group", OwnerId = ownerId };
        _context.Chats.Add(chat);
        _context.ChatMembers.AddRange(
            new ChatMember { ChatId = chat.Id, UserId = ownerId, Role = MemberRole.Owner },
            new ChatMember { ChatId = chat.Id, UserId = adminId, Role = MemberRole.Admin }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _chatService.RemoveMemberAsync(chat.Id, ownerId, adminId);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetOrCreatePrivateChatAsync Tests

    [Fact]
    public async Task GetOrCreatePrivateChatAsync_CreatesNewChat()
    {
        // Arrange
        var userId1 = 1L;
        var userId2 = 2L;

        // Act
        var result = await _chatService.GetOrCreatePrivateChatAsync(userId1, userId2);

        // Assert
        result.Should().NotBeNull();
        result!.Type.Should().Be(ChatType.Private);
    }

    [Fact]
    public async Task GetOrCreatePrivateChatAsync_ReturnsExistingChat()
    {
        // Arrange
        var userId1 = 1L;
        var userId2 = 2L;
        var existingChat = new Chat { Type = ChatType.Private, OwnerId = userId1 };
        _context.Chats.Add(existingChat);
        await _context.SaveChangesAsync();
        _context.ChatMembers.AddRange(
            new ChatMember { ChatId = existingChat.Id, UserId = userId1, Role = MemberRole.Member },
            new ChatMember { ChatId = existingChat.Id, UserId = userId2, Role = MemberRole.Member }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _chatService.GetOrCreatePrivateChatAsync(userId1, userId2);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(existingChat.Id);
    }

    [Fact]
    public async Task GetOrCreatePrivateChatAsync_AddsBothUsersAsMembers()
    {
        // Arrange
        var userId1 = 1L;
        var userId2 = 2L;

        // Act
        var result = await _chatService.GetOrCreatePrivateChatAsync(userId1, userId2);

        // Assert
        var members = await _context.ChatMembers
            .Where(cm => cm.ChatId == result!.Id).ToListAsync();
        members.Should().HaveCount(2);
        members.Should().Contain(m => m.UserId == userId1);
        members.Should().Contain(m => m.UserId == userId2);
    }

    #endregion

    #region UpdateMemberRoleAsync Tests

    [Fact]
    public async Task UpdateMemberRoleAsync_WithOwner_UpdatesRole()
    {
        // Arrange
        var ownerId = 1L;
        var memberId = 2L;
        var chat = new Chat { Type = ChatType.Group, Title = "Test Group", OwnerId = ownerId };
        _context.Chats.Add(chat);
        _context.ChatMembers.AddRange(
            new ChatMember { ChatId = chat.Id, UserId = ownerId, Role = MemberRole.Owner },
            new ChatMember { ChatId = chat.Id, UserId = memberId, Role = MemberRole.Member }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _chatService.UpdateMemberRoleAsync(chat.Id, memberId, MemberRole.Admin, ownerId);

        // Assert
        result.Should().BeTrue();
        var member = await _context.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == chat.Id && cm.UserId == memberId);
        member!.Role.Should().Be(MemberRole.Admin);
    }

    [Fact]
    public async Task UpdateMemberRoleAsync_CannotSetOwner_ReturnsFalse()
    {
        // Arrange
        var ownerId = 1L;
        var memberId = 2L;
        var chat = new Chat { Type = ChatType.Group, Title = "Test Group", OwnerId = ownerId };
        _context.Chats.Add(chat);
        _context.ChatMembers.AddRange(
            new ChatMember { ChatId = chat.Id, UserId = ownerId, Role = MemberRole.Owner },
            new ChatMember { ChatId = chat.Id, UserId = memberId, Role = MemberRole.Member }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _chatService.UpdateMemberRoleAsync(chat.Id, memberId, MemberRole.Owner, ownerId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateMemberRoleAsync_NonOwnerCannotUpdate_ReturnsFalse()
    {
        // Arrange
        var ownerId = 1L;
        var adminId = 2L;
        var memberId = 3L;
        var chat = new Chat { Type = ChatType.Group, Title = "Test Group", OwnerId = ownerId };
        _context.Chats.Add(chat);
        _context.ChatMembers.AddRange(
            new ChatMember { ChatId = chat.Id, UserId = ownerId, Role = MemberRole.Owner },
            new ChatMember { ChatId = chat.Id, UserId = adminId, Role = MemberRole.Admin },
            new ChatMember { ChatId = chat.Id, UserId = memberId, Role = MemberRole.Member }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _chatService.UpdateMemberRoleAsync(chat.Id, memberId, MemberRole.Admin, adminId);

        // Assert
        result.Should().BeFalse();
    }

    #endregion
}
