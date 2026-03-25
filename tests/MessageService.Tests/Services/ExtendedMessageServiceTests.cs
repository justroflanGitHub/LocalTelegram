using FluentAssertions;
using MessageService.Data;
using MessageService.Models;
using MessageService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MessageService.Tests.Services;

/// <summary>
/// Tests for extended MessageService features: reply, forward, reactions, pin, search
/// </summary>
public class ExtendedMessageServiceTests : IDisposable
{
    private readonly MessageDbContext _context;
    private readonly Mock<IRedisService> _redisServiceMock;
    private readonly Mock<IRabbitMqService> _rabbitMqServiceMock;
    private readonly Mock<ILogger<MessageService.Services.MessageService>> _loggerMock;
    private readonly MessageService.Services.MessageService _service;

    public ExtendedMessageServiceTests()
    {
        var options = new DbContextOptionsBuilder<MessageDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new MessageDbContext(options);
        _redisServiceMock = new Mock<IRedisService>();
        _rabbitMqServiceMock = new Mock<IRabbitMqService>();
        _loggerMock = new Mock<ILogger<MessageService.Services.MessageService>>();

        _service = new MessageService.Services.MessageService(
            _context,
            _redisServiceMock.Object,
            _rabbitMqServiceMock.Object,
            _loggerMock.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    #region Setup Helpers

    private async Task<(long ChatId, long UserId1, long UserId2)> SetupTestChatAsync()
    {
        var chat = new Chat
        {
            Type = ChatType.Private,
            CreatedAt = DateTime.UtcNow
        };
        _context.Chats.Add(chat);
        await _context.SaveChangesAsync();

        var member1 = new ChatMember
        {
            ChatId = chat.Id,
            UserId = 1,
            Role = MemberRole.Owner,
            JoinedAt = DateTime.UtcNow
        };
        var member2 = new ChatMember
        {
            ChatId = chat.Id,
            UserId = 2,
            Role = MemberRole.Member,
            JoinedAt = DateTime.UtcNow
        };
        _context.ChatMembers.AddRange(member1, member2);
        await _context.SaveChangesAsync();

        return (chat.Id, 1, 2);
    }

    private async Task<long> CreateTestMessageAsync(long chatId, long senderId, string content = "Test message")
    {
        var message = new Message
        {
            ChatId = chatId,
            SenderId = senderId,
            Content = content,
            ContentType = "text",
            Status = MessageStatus.Sent
        };
        _context.Messages.Add(message);
        await _context.SaveChangesAsync();
        return message.Id;
    }

    #endregion

    #region Reply Tests

    [Fact]
    public async Task ReplyToMessageAsync_ShouldCreateReplyMessage_WhenValidRequest()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();
        var originalMessageId = await CreateTestMessageAsync(chatId, userId, "Original message");

        // Act
        var result = await _service.ReplyToMessageAsync(userId, chatId, originalMessageId, "Reply content");

        // Assert
        result.Should().NotBeNull();
        result!.ReplyToId.Should().Be(originalMessageId);
        result.Content.Should().Be("Reply content");
        result.SenderId.Should().Be(userId);
        result.ChatId.Should().Be(chatId);

        _rabbitMqServiceMock.Verify(x => x.PublishMessageEventAsync("message.reply", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task ReplyToMessageAsync_ShouldReturnNull_WhenMessageNotFound()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();

        // Act
        var result = await _service.ReplyToMessageAsync(userId, chatId, 9999, "Reply content");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReplyToMessageAsync_ShouldReturnNull_WhenUserNotMemberOfChat()
    {
        // Arrange
        var (chatId, _, _) = await SetupTestChatAsync();
        var originalMessageId = await CreateTestMessageAsync(chatId, 1, "Original message");

        // Act - User 999 is not a member
        var result = await _service.ReplyToMessageAsync(999, chatId, originalMessageId, "Reply content");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Forward Tests

    [Fact]
    public async Task ForwardMessageAsync_ShouldCreateForwardedMessage_WhenValidRequest()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();
        var originalMessageId = await CreateTestMessageAsync(chatId, userId, "Message to forward");

        // Create target chat
        var targetChat = new Chat { Type = ChatType.Private, CreatedAt = DateTime.UtcNow };
        _context.Chats.Add(targetChat);
        await _context.SaveChangesAsync();

        var targetMember = new ChatMember
        {
            ChatId = targetChat.Id,
            UserId = userId,
            Role = MemberRole.Owner,
            JoinedAt = DateTime.UtcNow
        };
        _context.ChatMembers.Add(targetMember);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.ForwardMessageAsync(userId, originalMessageId, targetChat.Id);

        // Assert
        result.Should().NotBeNull();
        result!.ForwardFromId.Should().Be(originalMessageId);
        result.Content.Should().Be("Message to forward");
        result.ChatId.Should().Be(targetChat.Id);
        result.Metadata.Should().ContainKey("forwarded_from_chat");

        _rabbitMqServiceMock.Verify(x => x.PublishMessageEventAsync("message.forward", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task ForwardMessageAsync_ShouldReturnNull_WhenMessageNotFound()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();

        // Act
        var result = await _service.ForwardMessageAsync(userId, 9999, chatId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ForwardMessageAsync_ShouldReturnNull_WhenUserNotMemberOfTargetChat()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();
        var originalMessageId = await CreateTestMessageAsync(chatId, userId, "Message to forward");

        // Create target chat without adding user as member
        var targetChat = new Chat { Type = ChatType.Private, CreatedAt = DateTime.UtcNow };
        _context.Chats.Add(targetChat);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.ForwardMessageAsync(userId, originalMessageId, targetChat.Id);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ForwardMultipleMessagesAsync_ShouldForwardAllMessages_WhenValidRequest()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();
        var msg1 = await CreateTestMessageAsync(chatId, userId, "Message 1");
        var msg2 = await CreateTestMessageAsync(chatId, userId, "Message 2");
        var msg3 = await CreateTestMessageAsync(chatId, userId, "Message 3");

        // Create target chat
        var targetChat = new Chat { Type = ChatType.Private, CreatedAt = DateTime.UtcNow };
        _context.Chats.Add(targetChat);
        await _context.SaveChangesAsync();

        var targetMember = new ChatMember
        {
            ChatId = targetChat.Id,
            UserId = userId,
            Role = MemberRole.Owner,
            JoinedAt = DateTime.UtcNow
        };
        _context.ChatMembers.Add(targetMember);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.ForwardMultipleMessagesAsync(userId, new List<long> { msg1, msg2, msg3 }, targetChat.Id);

        // Assert
        result.Should().HaveCount(3);
        result.Should().BeInAscendingOrder(m => m.ForwardFromId);

        _rabbitMqServiceMock.Verify(x => x.PublishMessageEventAsync("message.forward_bulk", It.IsAny<object>()), Times.Once);
    }

    #endregion

    #region Search Tests

    [Fact]
    public async Task SearchMessagesAsync_ShouldReturnMatchingMessages_WhenQueryMatches()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();
        await CreateTestMessageAsync(chatId, userId, "Hello world");
        await CreateTestMessageAsync(chatId, userId, "Hello universe");
        await CreateTestMessageAsync(chatId, userId, "Goodbye world");

        // Act
        var result = await _service.SearchMessagesAsync(chatId, userId, "hello", 10);

        // Assert
        result.Should().HaveCount(2);
        result.All(m => m.Content!.Contains("Hello", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }

    [Fact]
    public async Task SearchMessagesAsync_ShouldReturnEmpty_WhenUserNotMember()
    {
        // Arrange
        var (chatId, _, _) = await SetupTestChatAsync();
        await CreateTestMessageAsync(chatId, 1, "Hello world");

        // Act - User 999 is not a member
        var result = await _service.SearchMessagesAsync(chatId, 999, "hello", 10);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchMessagesAsync_ShouldReturnEmpty_WhenQueryIsEmpty()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();
        await CreateTestMessageAsync(chatId, userId, "Hello world");

        // Act
        var result = await _service.SearchMessagesAsync(chatId, userId, "", 10);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchMessagesAsync_ShouldRespectLimit()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();
        for (int i = 0; i < 20; i++)
        {
            await CreateTestMessageAsync(chatId, userId, $"Message {i}");
        }

        // Act
        var result = await _service.SearchMessagesAsync(chatId, userId, "Message", 5);

        // Assert
        result.Should().HaveCount(5);
    }

    #endregion

    #region Pin Tests

    [Fact]
    public async Task PinMessageAsync_ShouldPinMessage_WhenUserIsAdmin()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();
        
        // Make user admin
        var member = await _context.ChatMembers.FirstAsync(cm => cm.ChatId == chatId && cm.UserId == userId);
        member.Role = MemberRole.Admin;
        await _context.SaveChangesAsync();

        var messageId = await CreateTestMessageAsync(chatId, userId, "Message to pin");

        // Act
        var result = await _service.PinMessageAsync(messageId, userId);

        // Assert
        result.Should().BeTrue();

        var message = await _context.Messages.FindAsync(messageId);
        message!.Metadata.Should().ContainKey("pinned");
        message.Metadata["pinned"].Should().Be(true);

        _rabbitMqServiceMock.Verify(x => x.PublishMessageEventAsync("message.pinned", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task PinMessageAsync_ShouldFail_WhenUserNotAdmin()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();
        var messageId = await CreateTestMessageAsync(chatId, userId, "Message to pin");

        // Act - User is regular member, not admin
        var result = await _service.PinMessageAsync(messageId, userId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UnpinMessageAsync_ShouldRemovePin_WhenMessageIsPinned()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();
        
        var member = await _context.ChatMembers.FirstAsync(cm => cm.ChatId == chatId && cm.UserId == userId);
        member.Role = MemberRole.Admin;
        await _context.SaveChangesAsync();

        var messageId = await CreateTestMessageAsync(chatId, userId, "Message to pin");
        await _service.PinMessageAsync(messageId, userId);

        // Act
        var result = await _service.UnpinMessageAsync(messageId, userId);

        // Assert
        result.Should().BeTrue();

        var message = await _context.Messages.FindAsync(messageId);
        message!.Metadata.Should().NotContainKey("pinned");

        _rabbitMqServiceMock.Verify(x => x.PublishMessageEventAsync("message.unpinned", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task GetPinnedMessagesAsync_ShouldReturnOnlyPinnedMessages()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();
        
        var member = await _context.ChatMembers.FirstAsync(cm => cm.ChatId == chatId && cm.UserId == userId);
        member.Role = MemberRole.Admin;
        await _context.SaveChangesAsync();

        var msg1 = await CreateTestMessageAsync(chatId, userId, "Message 1");
        var msg2 = await CreateTestMessageAsync(chatId, userId, "Message 2");
        var msg3 = await CreateTestMessageAsync(chatId, userId, "Message 3");

        await _service.PinMessageAsync(msg1, userId);
        await _service.PinMessageAsync(msg3, userId);

        // Act
        var result = await _service.GetPinnedMessagesAsync(chatId, userId);

        // Assert
        result.Should().HaveCount(2);
        result.Select(m => m.Id).Should().BeEquivalentTo(new[] { msg1, msg3 });
    }

    #endregion

    #region Reaction Tests

    [Fact]
    public async Task AddReactionAsync_ShouldAddReaction_WhenValidRequest()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();
        var messageId = await CreateTestMessageAsync(chatId, userId, "Message");

        // Act
        var result = await _service.AddReactionAsync(messageId, userId, "👍");

        // Assert
        result.Should().NotBeNull();
        result!.Emoji.Should().Be("👍");
        result.UserId.Should().Be(userId);
        result.MessageId.Should().Be(messageId);

        _rabbitMqServiceMock.Verify(x => x.PublishMessageEventAsync("message.reaction_added", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task AddReactionAsync_ShouldReturnExistingReaction_WhenAlreadyReacted()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();
        var messageId = await CreateTestMessageAsync(chatId, userId, "Message");
        await _service.AddReactionAsync(messageId, userId, "👍");

        // Act - Add same reaction again
        var result = await _service.AddReactionAsync(messageId, userId, "👍");

        // Assert
        result.Should().NotBeNull();
        
        // Should still only have one reaction
        var reactions = await _context.MessageReactions.CountAsync(r => r.MessageId == messageId && r.UserId == userId);
        reactions.Should().Be(1);
    }

    [Fact]
    public async Task AddReactionAsync_ShouldReturnNull_WhenMessageNotFound()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();

        // Act
        var result = await _service.AddReactionAsync(9999, userId, "👍");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task AddReactionAsync_ShouldReturnNull_WhenUserNotMemberOfChat()
    {
        // Arrange
        var (chatId, _, _) = await SetupTestChatAsync();
        var messageId = await CreateTestMessageAsync(chatId, 1, "Message");

        // Act - User 999 is not a member
        var result = await _service.AddReactionAsync(messageId, 999, "👍");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveReactionAsync_ShouldRemoveReaction_WhenExists()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();
        var messageId = await CreateTestMessageAsync(chatId, userId, "Message");
        await _service.AddReactionAsync(messageId, userId, "👍");

        // Act
        var result = await _service.RemoveReactionAsync(messageId, userId, "👍");

        // Assert
        result.Should().BeTrue();

        var reaction = await _context.MessageReactions.FirstOrDefaultAsync(r => r.MessageId == messageId && r.UserId == userId && r.Emoji == "👍");
        reaction.Should().BeNull();

        _rabbitMqServiceMock.Verify(x => x.PublishMessageEventAsync("message.reaction_removed", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task RemoveReactionAsync_ShouldReturnFalse_WhenReactionNotFound()
    {
        // Arrange
        var (chatId, userId, _) = await SetupTestChatAsync();
        var messageId = await CreateTestMessageAsync(chatId, userId, "Message");

        // Act
        var result = await _service.RemoveReactionAsync(messageId, userId, "👍");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetReactionsAsync_ShouldReturnAggregatedReactions()
    {
        // Arrange
        var (chatId, userId1, userId2) = await SetupTestChatAsync();
        var messageId = await CreateTestMessageAsync(chatId, userId1, "Message");

        await _service.AddReactionAsync(messageId, userId1, "👍");
        await _service.AddReactionAsync(messageId, userId2, "👍");
        await _service.AddReactionAsync(messageId, userId1, "❤️");

        // Act
        var result = await _service.GetReactionsAsync(messageId);

        // Assert
        result.Should().HaveCount(2);
        
        var thumbsUp = result.FirstOrDefault(r => r.Emoji == "👍");
        thumbsUp.Should().NotBeNull();
        thumbsUp!.Count.Should().Be(2);
        thumbsUp.UserIds.Should().BeEquivalentTo(new[] { userId1, userId2 });

        var heart = result.FirstOrDefault(r => r.Emoji == "❤️");
        heart.Should().NotBeNull();
        heart!.Count.Should().Be(1);
    }

    #endregion
}
