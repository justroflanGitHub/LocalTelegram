using FluentAssertions;
using ConferenceService.Data;
using ConferenceService.Models;
using ConferenceService.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace ConferenceService.Tests.Services;

public class ConferenceServiceTests : IDisposable
{
    private readonly ConferenceDbContext _dbContext;
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _redisDbMock;
    private readonly Mock<ILogger<ConferenceService.Services.ConferenceService>> _loggerMock;

    public ConferenceServiceTests()
    {
        var options = new DbContextOptionsBuilder<ConferenceDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ConferenceDbContext(options);

        _redisMock = new Mock<IConnectionMultiplexer>();
        _redisDbMock = new Mock<IDatabase>();
        _redisMock.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_redisDbMock.Object);
        _loggerMock = new Mock<ILogger<ConferenceService.Services.ConferenceService>>();
    }

    #region Room Management Tests

    [Fact]
    public async Task CreateRoom_ShouldCreateRoomWithCorrectProperties()
    {
        // Arrange
        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);
        var request = new CreateRoomRequest
        {
            Title = "Test Conference",
            Type = ConferenceType.Video,
            MaxParticipants = 50,
            VideoEnabled = true,
            AudioEnabled = true
        };

        // Act
        var result = await service.CreateRoomAsync(request, userId: 1);

        // Assert
        result.Should().NotBeNull();
        result.Title.Should().Be("Test Conference");
        result.Type.Should().Be(ConferenceType.Video);
        result.MaxParticipants.Should().Be(50);
        result.CreatorId.Should().Be(1);
        result.Status.Should().Be(ConferenceStatus.Waiting);
        result.RoomCode.Should().HaveLength(8);
        result.HasPassword.Should().BeFalse();
    }

    [Fact]
    public async Task CreateRoom_WithPassword_ShouldSetHasPassword()
    {
        // Arrange
        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);
        var request = new CreateRoomRequest
        {
            Title = "Private Room",
            Password = "secret123"
        };

        // Act
        var result = await service.CreateRoomAsync(request, userId: 1);

        // Assert
        result.HasPassword.Should().BeTrue();
        result.PasswordHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetRoom_WhenExists_ReturnsRoom()
    {
        // Arrange
        var room = new ConferenceRoom
        {
            RoomCode = "ABCD1234",
            Title = "Test Room",
            CreatorId = 1,
            Status = ConferenceStatus.Active
        };
        _dbContext.ConferenceRooms.Add(room);
        await _dbContext.SaveChangesAsync();

        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);

        // Act
        var result = await service.GetRoomAsync(room.Id);

        // Assert
        result.Should().NotBeNull();
        result!.RoomCode.Should().Be("ABCD1234");
    }

    [Fact]
    public async Task GetRoom_WhenNotExists_ReturnsNull()
    {
        // Arrange
        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);

        // Act
        var result = await service.GetRoomAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRoomByCode_ShouldReturnCorrectRoom()
    {
        // Arrange
        var room = new ConferenceRoom
        {
            RoomCode = "TESTCODE",
            Title = "Test Room",
            CreatorId = 1
        };
        _dbContext.ConferenceRooms.Add(room);
        await _dbContext.SaveChangesAsync();

        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);

        // Act
        var result = await service.GetRoomByCodeAsync("TESTCODE");

        // Assert
        result.Should().NotBeNull();
        result!.RoomCode.Should().Be("TESTCODE");
    }

    [Fact]
    public async Task JoinRoom_ShouldAddParticipant()
    {
        // Arrange
        var room = new ConferenceRoom
        {
            RoomCode = "JOINTEST",
            Title = "Join Test",
            CreatorId = 1,
            Status = ConferenceStatus.Waiting,
            MaxParticipants = 10
        };
        _dbContext.ConferenceRooms.Add(room);
        await _dbContext.SaveChangesAsync();

        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);

        // Act
        var result = await service.JoinRoomAsync(room.Id, userId: 2);

        // Assert
        result.Should().NotBeNull();
        result.Participants.Should().HaveCount(1);
        result.Status.Should().Be(ConferenceStatus.Active); // Status changes to Active when first participant joins
    }

    [Fact]
    public async Task JoinRoom_WithPassword_ShouldValidatePassword()
    {
        // Arrange
        var room = new ConferenceRoom
        {
            RoomCode = "PASSTEST",
            Title = "Password Protected",
            CreatorId = 1,
            HasPassword = true,
            PasswordHash = HashPasswordHelper("correctpass")
        };
        _dbContext.ConferenceRooms.Add(room);
        await _dbContext.SaveChangesAsync();

        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);

        // Act & Assert
        var actWrong = async () => await service.JoinRoomAsync(room.Id, userId: 2, "wrongpass");
        await actWrong.Should().ThrowAsync<InvalidOperationException>().WithMessage("Invalid password");

        var actCorrect = async () => await service.JoinRoomAsync(room.Id, userId: 2, "correctpass");
        await actCorrect.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LeaveRoom_ShouldUpdateParticipantAndCount()
    {
        // Arrange
        var room = new ConferenceRoom
        {
            RoomCode = "LEAVETEST",
            Title = "Leave Test",
            CreatorId = 1,
            Status = ConferenceStatus.Active,
            ParticipantCount = 2
        };
        var participant = new ConferenceParticipant
        {
            RoomId = 1,
            UserId = 2,
            JoinedAt = DateTime.UtcNow
        };
        _dbContext.ConferenceRooms.Add(room);
        _dbContext.ConferenceParticipants.Add(participant);
        await _dbContext.SaveChangesAsync();

        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);

        // Act
        await service.LeaveRoomAsync(room.Id, userId: 2);

        // Assert
        var updatedParticipant = await _dbContext.ConferenceParticipants.FindAsync(participant.Id);
        updatedParticipant!.LeftAt.Should().NotBeNull();
    }

    [Fact]
    public async Task EndRoom_ShouldEndRoomAndRemoveAllParticipants()
    {
        // Arrange
        var room = new ConferenceRoom
        {
            RoomCode = "ENDTEST",
            Title = "End Test",
            CreatorId = 1,
            Status = ConferenceStatus.Active,
            ParticipantCount = 2
        };
        var participant1 = new ConferenceParticipant { RoomId = 1, UserId = 1 };
        var participant2 = new ConferenceParticipant { RoomId = 1, UserId = 2 };
        _dbContext.ConferenceRooms.Add(room);
        _dbContext.ConferenceParticipants.AddRange(participant1, participant2);
        await _dbContext.SaveChangesAsync();

        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);

        // Act
        await service.EndRoomAsync(room.Id, userId: 1);

        // Assert
        var updatedRoom = await _dbContext.ConferenceRooms.FindAsync(room.Id);
        updatedRoom!.Status.Should().Be(ConferenceStatus.Ended);
        updatedRoom.EndTime.Should().NotBeNull();
        updatedRoom.ParticipantCount.Should().Be(0);
    }

    #endregion

    #region 1-on-1 Call Tests

    [Fact]
    public async Task StartCall_ShouldCreateCallSession()
    {
        // Arrange
        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);
        var request = new StartCallRequest
        {
            CalleeId = 2,
            ChatId = 100,
            Type = CallType.Video
        };

        // Act
        var result = await service.StartCallAsync(request, callerId: 1);

        // Assert
        result.Should().NotBeNull();
        result.CallerId.Should().Be(1);
        result.CalleeId.Should().Be(2);
        result.ChatId.Should().Be(100);
        result.Type.Should().Be(CallType.Video);
        result.Status.Should().Be(CallStatus.Ringing);
    }

    [Fact]
    public async Task StartCall_WhenActiveCallExists_ShouldThrow()
    {
        // Arrange
        var existingCall = new CallSession
        {
            CallerId = 1,
            CalleeId = 2,
            ChatId = 100,
            Status = CallStatus.Ringing
        };
        _dbContext.CallSessions.Add(existingCall);
        await _dbContext.SaveChangesAsync();

        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);
        var request = new StartCallRequest { CalleeId = 2, ChatId = 100 };

        // Act
        var act = async () => await service.StartCallAsync(request, callerId: 3);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("There is already an active call in this chat");
    }

    [Fact]
    public async Task AnswerCall_WhenAccepting_ShouldUpdateStatus()
    {
        // Arrange
        var call = new CallSession
        {
            CallerId = 1,
            CalleeId = 2,
            ChatId = 100,
            Status = CallStatus.Ringing
        };
        _dbContext.CallSessions.Add(call);
        await _dbContext.SaveChangesAsync();

        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);

        // Act
        var result = await service.AnswerCallAsync(call.Id, calleeId: 2, accept: true);

        // Assert
        result.Status.Should().Be(CallStatus.Answered);
        result.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AnswerCall_WhenDeclining_ShouldSetDeclinedStatus()
    {
        // Arrange
        var call = new CallSession
        {
            CallerId = 1,
            CalleeId = 2,
            ChatId = 100,
            Status = CallStatus.Ringing
        };
        _dbContext.CallSessions.Add(call);
        await _dbContext.SaveChangesAsync();

        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);

        // Act
        var result = await service.AnswerCallAsync(call.Id, calleeId: 2, accept: false);

        // Assert
        result.Status.Should().Be(CallStatus.Declined);
        result.EndedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task EndCall_ShouldSetEndedStatusAndDuration()
    {
        // Arrange
        var call = new CallSession
        {
            CallerId = 1,
            CalleeId = 2,
            ChatId = 100,
            Status = CallStatus.Answered,
            StartedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        _dbContext.CallSessions.Add(call);
        await _dbContext.SaveChangesAsync();

        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);

        // Act
        await service.EndCallAsync(call.Id, userId: 1, CallEndReason.UserHangup);

        // Assert
        var updatedCall = await _dbContext.CallSessions.FindAsync(call.Id);
        updatedCall!.Status.Should().Be(CallStatus.Ended);
        updatedCall.EndReason.Should().Be(CallEndReason.UserHangup);
        updatedCall.DurationSeconds.Should().BeGreaterThan(0);
    }

    #endregion

    #region ICE Servers Tests

    [Fact]
    public async Task GetIceServers_ShouldReturnActiveServers()
    {
        // Arrange
        var servers = new List<IceServer>
        {
            new() { Url = "stun:stun.l.google.com:19302", Type = IceServerType.Stun, IsActive = true, Priority = 10 },
            new() { Url = "turn:turn.example.com:3478", Type = IceServerType.Turn, IsActive = true, Priority = 20, Username = "user", Credential = "pass" },
            new() { Url = "stun:stun.old.com:19302", Type = IceServerType.Stun, IsActive = false, Priority = 30 }
        };
        _dbContext.IceServers.AddRange(servers);
        await _dbContext.SaveChangesAsync();

        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);

        // Act
        var result = await service.GetIceServersAsync();

        // Assert
        result.Should().HaveCount(2); // Only active servers
        result[0].Url.Should().Be("stun:stun.l.google.com:19302");
        result[1].Url.Should().Be("turn:turn.example.com:3478");
    }

    [Fact]
    public async Task GetWebRtcConfig_ShouldIncludeIceServers()
    {
        // Arrange
        var server = new IceServer
        {
            Url = "stun:stun.test.com:19302",
            Type = IceServerType.Stun,
            IsActive = true,
            Priority = 10
        };
        _dbContext.IceServers.Add(server);
        await _dbContext.SaveChangesAsync();

        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);

        // Act
        var result = await service.GetWebRtcConfigAsync();

        // Assert
        result.Should().NotBeNull();
        result.IceServers.Should().NotBeEmpty();
    }

    #endregion

    #region Participant Management Tests

    [Fact]
    public async Task UpdateParticipantMedia_ShouldUpdateVideoAndAudioState()
    {
        // Arrange
        var room = new ConferenceRoom { RoomCode = "MEDIATEST", CreatorId = 1 };
        var participant = new ConferenceParticipant
        {
            RoomId = 1,
            UserId = 2,
            VideoEnabled = true,
            AudioEnabled = true
        };
        _dbContext.ConferenceRooms.Add(room);
        _dbContext.ConferenceParticipants.Add(participant);
        await _dbContext.SaveChangesAsync();

        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);

        // Act
        await service.UpdateParticipantMediaAsync(room.Id, userId: 2, videoEnabled: false, audioEnabled: false);

        // Assert
        var updated = await _dbContext.ConferenceParticipants.FindAsync(participant.Id);
        updated!.VideoEnabled.Should().BeFalse();
        updated.AudioEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task SetHandRaised_ShouldUpdateHandState()
    {
        // Arrange
        var room = new ConferenceRoom { RoomCode = "HANDTEST", CreatorId = 1 };
        var participant = new ConferenceParticipant { RoomId = 1, UserId = 2, HandRaised = false };
        _dbContext.ConferenceRooms.Add(room);
        _dbContext.ConferenceParticipants.Add(participant);
        await _dbContext.SaveChangesAsync();

        var service = new ConferenceService.Services.ConferenceService(_dbContext, _redisMock.Object, _loggerMock.Object);

        // Act
        await service.SetHandRaisedAsync(room.Id, userId: 2, isRaised: true);

        // Assert
        var updated = await _dbContext.ConferenceParticipants.FindAsync(participant.Id);
        updated!.HandRaised.Should().BeTrue();
    }

    #endregion

    private static string HashPasswordHelper(string password)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }
}
