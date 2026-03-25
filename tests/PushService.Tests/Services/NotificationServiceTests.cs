using FluentAssertions;
using Moq;
using PushService.Services;
using StackExchange.Redis;
using System.Text.Json;
using Xunit;

namespace PushService.Tests.Services;

public class NotificationServiceTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _dbMock;
    private readonly Mock<ILogger<NotificationService>> _loggerMock;
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _dbMock = new Mock<IDatabase>();
        _loggerMock = new Mock<ILogger<NotificationService>>();
        
        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
        _service = new NotificationService(_redisMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task StoreNotificationAsync_ShouldStoreNotification()
    {
        // Arrange
        var notification = new Notification
        {
            Id = 1,
            UserId = 100,
            Type = "message",
            Title = "New Message",
            Body = "You have a new message",
            CreatedAt = DateTime.UtcNow
        };

        _dbMock.Setup(db => db.ListLeftPushAsync(
            It.IsAny<RedisKey>(), 
            It.IsAny<RedisValue>(), 
            It.IsAny<When>(), 
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        _dbMock.Setup(db => db.ListTrimAsync(
            It.IsAny<RedisKey>(), 
            It.IsAny<long>(), 
            It.IsAny<long>(), 
            It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);

        _dbMock.Setup(db => db.KeyExpireAsync(
            It.IsAny<RedisKey>(), 
            It.IsAny<TimeSpan>(), 
            It.IsAny<ExpireWhen>(), 
            It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);

        _dbMock.Setup(db => db.SetAddAsync(
            It.IsAny<RedisKey>(), 
            It.IsAny<RedisValue>(), 
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _service.StoreNotificationAsync(notification);

        // Assert
        _dbMock.Verify(db => db.ListLeftPushAsync(
            It.Is<RedisKey>(k => k == "notifications:100"),
            It.IsAny<RedisValue>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);

        _dbMock.Verify(db => db.SetAddAsync(
            It.Is<RedisKey>(k => k == "notifications:100:unread"),
            It.Is<RedisValue>(v => v == 1),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task GetPendingNotificationsAsync_ShouldReturnNotifications()
    {
        // Arrange
        var userId = 100L;
        var notifications = new List<Notification>
        {
            new() { Id = 1, UserId = userId, Type = "message", Title = "Test 1", Body = "Body 1" },
            new() { Id = 2, UserId = userId, Type = "message", Title = "Test 2", Body = "Body 2" }
        };

        var redisValues = notifications
            .Select(n => new RedisValue(JsonSerializer.Serialize(n)))
            .ToArray();

        _dbMock.Setup(db => db.ListRangeAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(redisValues);

        // Act
        var result = await _service.GetPendingNotificationsAsync(userId);

        // Assert
        result.Should().HaveCount(2);
        result[0].Id.Should().Be(1);
        result[1].Id.Should().Be(2);
    }

    [Fact]
    public async Task GetPendingNotificationsAsync_WhenNoNotifications_ShouldReturnEmptyList()
    {
        // Arrange
        var userId = 100L;

        _dbMock.Setup(db => db.ListRangeAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());

        // Act
        var result = await _service.GetPendingNotificationsAsync(userId);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkAsReadAsync_ShouldRemoveFromUnreadSet()
    {
        // Arrange
        var notificationId = 1L;
        var userId = 100L;

        _dbMock.Setup(db => db.SetRemoveAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _dbMock.Setup(db => db.ListRangeAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());

        // Act
        await _service.MarkAsReadAsync(notificationId, userId);

        // Assert
        _dbMock.Verify(db => db.SetRemoveAsync(
            It.Is<RedisKey>(k => k == "notifications:100:unread"),
            It.Is<RedisValue>(v => v == notificationId),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_ShouldDeleteUnreadSet()
    {
        // Arrange
        var userId = 100L;

        _dbMock.Setup(db => db.KeyDeleteAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _dbMock.Setup(db => db.ListRangeAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());

        // Act
        await _service.MarkAllAsReadAsync(userId);

        // Assert
        _dbMock.Verify(db => db.KeyDeleteAsync(
            It.Is<RedisKey>(k => k == "notifications:100:unread"),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task GetUnreadCountAsync_ShouldReturnCount()
    {
        // Arrange
        var userId = 100L;
        var expectedCount = 5L;

        _dbMock.Setup(db => db.SetLengthAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(expectedCount);

        // Act
        var result = await _service.GetUnreadCountAsync(userId);

        // Assert
        result.Should().Be(5);
    }

    [Fact]
    public async Task DeleteNotificationAsync_ShouldRemoveFromUnreadAndList()
    {
        // Arrange
        var notificationId = 1L;
        var userId = 100L;

        _dbMock.Setup(db => db.SetRemoveAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _dbMock.Setup(db => db.ListRangeAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());

        // Act
        await _service.DeleteNotificationAsync(notificationId, userId);

        // Assert
        _dbMock.Verify(db => db.SetRemoveAsync(
            It.Is<RedisKey>(k => k == "notifications:100:unread"),
            It.Is<RedisValue>(v => v == notificationId),
            It.IsAny<CommandFlags>()), Times.Once);
    }
}
