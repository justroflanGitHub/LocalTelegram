using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PushService.Services;
using StackExchange.Redis;
using System.Text.Json;
using Xunit;

namespace PushService.Tests.Services;

public class ChatMuteServiceTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _dbMock;
    private readonly Mock<ILogger<ChatMuteService>> _loggerMock;
    private readonly ChatMuteService _service;

    public ChatMuteServiceTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _dbMock = new Mock<IDatabase>();
        _loggerMock = new Mock<ILogger<ChatMuteService>>();

        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
        _service = new ChatMuteService(_redisMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task MuteChatAsync_StoresMuteInRedis()
    {
        // Arrange
        var userId = 1L;
        var chatId = 100L;

        // Act
        await _service.MuteChatAsync(userId, chatId);

        // Assert
        _dbMock.Verify(db => db.StringSetAsync(
            It.Is<RedisKey>(k => k == $"mute:{userId}:{chatId}"),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);

        _dbMock.Verify(db => db.SetAddAsync(
            It.Is<RedisKey>(k => k == $"user_mutes:{userId}"),
            It.Is<RedisValue>(v => v == chatId.ToString()),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task MuteChatAsync_WithDuration_SetsExpiration()
    {
        // Arrange
        var userId = 1L;
        var chatId = 100L;
        var duration = TimeSpan.FromHours(1);

        // Act
        await _service.MuteChatAsync(userId, chatId, duration);

        // Assert
        _dbMock.Verify(db => db.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.Is<TimeSpan?>(t => t == duration),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task UnmuteChatAsync_RemovesMuteFromRedis()
    {
        // Arrange
        var userId = 1L;
        var chatId = 100L;

        // Act
        await _service.UnmuteChatAsync(userId, chatId);

        // Assert
        _dbMock.Verify(db => db.KeyDeleteAsync(
            It.Is<RedisKey>(k => k == $"mute:{userId}:{chatId}"),
            It.IsAny<CommandFlags>()), Times.Once);

        _dbMock.Verify(db => db.SetRemoveAsync(
            It.Is<RedisKey>(k => k == $"user_mutes:{userId}"),
            It.Is<RedisValue>(v => v == chatId.ToString()),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task IsChatMutedAsync_WhenNoMute_ReturnsFalse()
    {
        // Arrange
        var userId = 1L;
        var chatId = 100L;

        _dbMock.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await _service.IsChatMutedAsync(userId, chatId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsChatMutedAsync_WhenActiveMute_ReturnsTrue()
    {
        // Arrange
        var userId = 1L;
        var chatId = 100L;
        var mute = new ChatMute
        {
            UserId = userId,
            ChatId = chatId,
            MutedAt = DateTime.UtcNow,
            MuteForever = true
        };
        var json = JsonSerializer.Serialize(mute);

        _dbMock.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(json);

        // Act
        var result = await _service.IsChatMutedAsync(userId, chatId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsChatMutedAsync_WhenExpiredMute_ReturnsFalseAndRemoves()
    {
        // Arrange
        var userId = 1L;
        var chatId = 100L;
        var mute = new ChatMute
        {
            UserId = userId,
            ChatId = chatId,
            MutedAt = DateTime.UtcNow.AddHours(-2),
            MutedUntil = DateTime.UtcNow.AddHours(-1),
            MuteForever = false
        };
        var json = JsonSerializer.Serialize(mute);

        _dbMock.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(json);

        // Act
        var result = await _service.IsChatMutedAsync(userId, chatId);

        // Assert
        result.Should().BeFalse();
        _dbMock.Verify(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Once);
    }
}

public class BadgeCountServiceTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _dbMock;
    private readonly Mock<ILogger<BadgeCountService>> _loggerMock;
    private readonly BadgeCountService _service;

    public BadgeCountServiceTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _dbMock = new Mock<IDatabase>();
        _loggerMock = new Mock<ILogger<BadgeCountService>>();

        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
        _service = new BadgeCountService(_redisMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task IncrementBadgeCountAsync_IncrementsInRedis()
    {
        // Arrange
        var userId = 1L;
        var chatId = 100L;

        // Act
        await _service.IncrementBadgeCountAsync(userId, chatId);

        // Assert
        _dbMock.Verify(db => db.StringIncrementAsync(
            It.Is<RedisKey>(k => k == $"badge:{userId}:{chatId}"),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()), Times.Once);

        _dbMock.Verify(db => db.SetAddAsync(
            It.Is<RedisKey>(k => k == $"user_badges:{userId}"),
            It.Is<RedisValue>(v => v == chatId.ToString()),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task IncrementBadgeCountAsync_WithCount_IncrementsByCount()
    {
        // Arrange
        var userId = 1L;
        var chatId = 100L;
        var count = 5;

        // Act
        await _service.IncrementBadgeCountAsync(userId, chatId, count);

        // Assert
        _dbMock.Verify(db => db.StringIncrementAsync(
            It.IsAny<RedisKey>(),
            It.Is<long>(c => c == count),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task DecrementBadgeCountAsync_DecrementsInRedis()
    {
        // Arrange
        var userId = 1L;
        var chatId = 100L;

        _dbMock.Setup(db => db.StringDecrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(5);

        // Act
        await _service.DecrementBadgeCountAsync(userId, chatId);

        // Assert
        _dbMock.Verify(db => db.StringDecrementAsync(
            It.Is<RedisKey>(k => k == $"badge:{userId}:{chatId}"),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task ResetBadgeCountAsync_RemovesFromRedis()
    {
        // Arrange
        var userId = 1L;
        var chatId = 100L;

        // Act
        await _service.ResetBadgeCountAsync(userId, chatId);

        // Assert
        _dbMock.Verify(db => db.KeyDeleteAsync(
            It.Is<RedisKey>(k => k == $"badge:{userId}:{chatId}"),
            It.IsAny<CommandFlags>()), Times.Once);

        _dbMock.Verify(db => db.SetRemoveAsync(
            It.Is<RedisKey>(k => k == $"user_badges:{userId}"),
            It.Is<RedisValue>(v => v == chatId.ToString()),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task GetChatBadgeCountAsync_ReturnsCount()
    {
        // Arrange
        var userId = 1L;
        var chatId = 100L;

        _dbMock.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(7));

        // Act
        var result = await _service.GetChatBadgeCountAsync(userId, chatId);

        // Assert
        result.Should().Be(7);
    }

    [Fact]
    public async Task GetChatBadgeCountAsync_WhenNoBadge_ReturnsZero()
    {
        // Arrange
        var userId = 1L;
        var chatId = 100L;

        _dbMock.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await _service.GetChatBadgeCountAsync(userId, chatId);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task GetTotalBadgeCountAsync_SumsAllBadges()
    {
        // Arrange
        var userId = 1L;

        var chatIds = new RedisValue[] { "100", "200", "300" };
        _dbMock.Setup(db => db.SetMembersAsync(It.Is<RedisKey>(k => k == $"user_badges:{userId}"), It.IsAny<CommandFlags>()))
            .ReturnsAsync(chatIds);

        _dbMock.Setup(db => db.StringGetAsync(It.Is<RedisKey>(k => k == $"badge:{userId}:100"), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(3));
        _dbMock.Setup(db => db.StringGetAsync(It.Is<RedisKey>(k => k == $"badge:{userId}:200"), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(5));
        _dbMock.Setup(db => db.StringGetAsync(It.Is<RedisKey>(k => k == $"badge:{userId}:300"), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(2));

        // Act
        var result = await _service.GetTotalBadgeCountAsync(userId);

        // Assert
        result.Should().Be(10);
    }

    [Fact]
    public async Task ResetAllBadgeCountsAsync_ClearsAllBadges()
    {
        // Arrange
        var userId = 1L;
        var chatIds = new RedisValue[] { "100", "200" };

        _dbMock.Setup(db => db.SetMembersAsync(It.Is<RedisKey>(k => k == $"user_badges:{userId}"), It.IsAny<CommandFlags>()))
            .ReturnsAsync(chatIds);

        // Act
        await _service.ResetAllBadgeCountsAsync(userId);

        // Assert
        _dbMock.Verify(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Exactly(3));
    }
}
