using ConferenceService.Data;
using ConferenceService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;

namespace ConferenceService.Services;

/// <summary>
/// Service for managing conference rooms and calls
/// </summary>
public interface IConferenceService
{
    // Room management
    Task<ConferenceRoom> CreateRoomAsync(CreateRoomRequest request, long creatorId);
    Task<ConferenceRoom?> GetRoomAsync(long roomId);
    Task<ConferenceRoom?> GetRoomByCodeAsync(string roomCode);
    Task<RoomDto> JoinRoomAsync(long roomId, long userId, string? password = null);
    Task LeaveRoomAsync(long roomId, long userId);
    Task EndRoomAsync(long roomId, long userId);
    Task<List<RoomDto>> GetActiveRoomsAsync(long? chatId = null);
    
    // Participant management
    Task UpdateParticipantMediaAsync(long roomId, long userId, bool? videoEnabled = null, bool? audioEnabled = null);
    Task SetScreenSharingAsync(long roomId, long userId, bool isSharing);
    Task SetHandRaisedAsync(long roomId, long userId, bool isRaised);
    Task SetParticipantRoleAsync(long roomId, long userId, ParticipantRole role);
    Task<List<ParticipantDto>> GetParticipantsAsync(long roomId);
    
    // 1-on-1 calls
    Task<CallSession> StartCallAsync(StartCallRequest request, long callerId);
    Task<CallSession> AnswerCallAsync(long callId, long calleeId, bool accept);
    Task EndCallAsync(long callId, long userId, CallEndReason reason = CallEndReason.UserHangup);
    Task<CallSession?> GetActiveCallAsync(long chatId);
    Task<CallSession?> GetCallAsync(long callId);
    
    // ICE servers
    Task<List<IceServerDto>> GetIceServersAsync();
    Task<WebRtcConfig> GetWebRtcConfigAsync();
}

public class ConferenceService : IConferenceService
{
    private readonly ConferenceDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ConferenceService> _logger;
    private readonly IDatabase _redisDb;

    public ConferenceService(
        ConferenceDbContext dbContext,
        IConnectionMultiplexer redis,
        ILogger<ConferenceService> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;
        _redisDb = redis.GetDatabase();
    }

    #region Room Management

    public async Task<ConferenceRoom> CreateRoomAsync(CreateRoomRequest request, long creatorId)
    {
        var roomCode = GenerateRoomCode();
        
        // Ensure unique room code
        while (await _dbContext.ConferenceRooms.AnyAsync(r => r.RoomCode == roomCode))
        {
            roomCode = GenerateRoomCode();
        }

        var room = new ConferenceRoom
        {
            RoomCode = roomCode,
            Title = request.Title,
            ChatId = request.ChatId,
            CreatorId = creatorId,
            Type = request.Type,
            MaxParticipants = request.MaxParticipants,
            HasPassword = !string.IsNullOrEmpty(request.Password),
            PasswordHash = string.IsNullOrEmpty(request.Password) ? null : HashPassword(request.Password),
            VideoEnabled = request.VideoEnabled,
            AudioEnabled = request.AudioEnabled,
            ScreenShareEnabled = request.ScreenShareEnabled,
            RecordingEnabled = request.RecordingEnabled,
            Status = ConferenceStatus.Waiting,
            StartTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ConferenceRooms.Add(room);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Created conference room {RoomId} with code {RoomCode}", room.Id, room.RoomCode);

        return room;
    }

    public async Task<ConferenceRoom?> GetRoomAsync(long roomId)
    {
        return await _dbContext.ConferenceRooms
            .Include(r => r.Participants)
            .FirstOrDefaultAsync(r => r.Id == roomId);
    }

    public async Task<ConferenceRoom?> GetRoomByCodeAsync(string roomCode)
    {
        return await _dbContext.ConferenceRooms
            .Include(r => r.Participants)
            .FirstOrDefaultAsync(r => r.RoomCode == roomCode);
    }

    public async Task<RoomDto> JoinRoomAsync(long roomId, long userId, string? password = null)
    {
        var room = await _dbContext.ConferenceRooms
            .Include(r => r.Participants)
            .FirstOrDefaultAsync(r => r.Id == roomId);

        if (room == null)
            throw new InvalidOperationException("Room not found");

        if (room.Status == ConferenceStatus.Ended)
            throw new InvalidOperationException("Room has ended");

        if (room.HasPassword && !VerifyPassword(password ?? "", room.PasswordHash ?? ""))
            throw new InvalidOperationException("Invalid password");

        // Check if already in room
        var existingParticipant = room.Participants.FirstOrDefault(p => p.UserId == userId && p.LeftAt == null);
        if (existingParticipant != null)
        {
            return await MapToRoomDto(room);
        }

        // Check capacity
        var activeCount = room.Participants.Count(p => p.LeftAt == null);
        if (activeCount >= room.MaxParticipants)
            throw new InvalidOperationException("Room is full");

        // Add participant
        var participant = new ConferenceParticipant
        {
            RoomId = roomId,
            UserId = userId,
            Role = userId == room.CreatorId ? ParticipantRole.Admin : ParticipantRole.Member,
            VideoEnabled = room.VideoEnabled,
            AudioEnabled = room.AudioEnabled,
            JoinedAt = DateTime.UtcNow
        };

        _dbContext.ConferenceParticipants.Add(participant);
        
        // Update room status and count
        room.ParticipantCount = activeCount + 1;
        if (room.Status == ConferenceStatus.Waiting)
            room.Status = ConferenceStatus.Active;

        await _dbContext.SaveChangesAsync();

        // Cache participant in Redis for quick lookup
        await _redisDb.HashSetAsync($"room:{roomId}:participants", userId.ToString(), participant.Id.ToString());

        _logger.LogInformation("User {UserId} joined room {RoomId}", userId, roomId);

        return await MapToRoomDto(room);
    }

    public async Task LeaveRoomAsync(long roomId, long userId)
    {
        var participant = await _dbContext.ConferenceParticipants
            .FirstOrDefaultAsync(p => p.RoomId == roomId && p.UserId == userId && p.LeftAt == null);

        if (participant == null)
            return;

        participant.LeftAt = DateTime.UtcNow;

        var room = await _dbContext.ConferenceRooms.FindAsync(roomId);
        if (room != null)
        {
            room.ParticipantCount = Math.Max(0, room.ParticipantCount - 1);

            // End room if no participants left
            if (room.ParticipantCount == 0)
            {
                room.Status = ConferenceStatus.Ended;
                room.EndTime = DateTime.UtcNow;
                _logger.LogInformation("Room {RoomId} ended (no participants)", roomId);
            }
        }

        await _redisDb.HashDeleteAsync($"room:{roomId}:participants", userId.ToString());
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("User {UserId} left room {RoomId}", userId, roomId);
    }

    public async Task EndRoomAsync(long roomId, long userId)
    {
        var room = await _dbContext.ConferenceRooms.FindAsync(roomId);
        if (room == null)
            throw new InvalidOperationException("Room not found");

        if (room.CreatorId != userId)
            throw new InvalidOperationException("Only room creator can end the room");

        // Mark all participants as left
        var participants = await _dbContext.ConferenceParticipants
            .Where(p => p.RoomId == roomId && p.LeftAt == null)
            .ToListAsync();

        foreach (var participant in participants)
        {
            participant.LeftAt = DateTime.UtcNow;
        }

        room.Status = ConferenceStatus.Ended;
        room.EndTime = DateTime.UtcNow;
        room.ParticipantCount = 0;

        // Clear Redis cache
        await _redisDb.KeyDeleteAsync($"room:{roomId}:participants");

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Room {RoomId} ended by creator {UserId}", roomId, userId);
    }

    public async Task<List<RoomDto>> GetActiveRoomsAsync(long? chatId = null)
    {
        var query = _dbContext.ConferenceRooms
            .Include(r => r.Participants)
            .Where(r => r.Status == ConferenceStatus.Active || r.Status == ConferenceStatus.Waiting);

        if (chatId.HasValue)
            query = query.Where(r => r.ChatId == chatId);

        var rooms = await query.ToListAsync();
        var result = new List<RoomDto>();

        foreach (var room in rooms)
        {
            result.Add(await MapToRoomDto(room));
        }

        return result;
    }

    #endregion

    #region Participant Management

    public async Task UpdateParticipantMediaAsync(long roomId, long userId, bool? videoEnabled = null, bool? audioEnabled = null)
    {
        var participant = await _dbContext.ConferenceParticipants
            .FirstOrDefaultAsync(p => p.RoomId == roomId && p.UserId == userId && p.LeftAt == null);

        if (participant == null)
            throw new InvalidOperationException("Participant not found in room");

        if (videoEnabled.HasValue)
            participant.VideoEnabled = videoEnabled.Value;

        if (audioEnabled.HasValue)
            participant.AudioEnabled = audioEnabled.Value;

        await _dbContext.SaveChangesAsync();
    }

    public async Task SetScreenSharingAsync(long roomId, long userId, bool isSharing)
    {
        // Stop any other screen shares in the room
        var otherSharing = await _dbContext.ConferenceParticipants
            .Where(p => p.RoomId == roomId && p.IsScreenSharing && p.UserId != userId && p.LeftAt == null)
            .ToListAsync();

        foreach (var p in otherSharing)
        {
            p.IsScreenSharing = false;
        }

        var participant = await _dbContext.ConferenceParticipants
            .FirstOrDefaultAsync(p => p.RoomId == roomId && p.UserId == userId && p.LeftAt == null);

        if (participant != null)
        {
            participant.IsScreenSharing = isSharing;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task SetHandRaisedAsync(long roomId, long userId, bool isRaised)
    {
        var participant = await _dbContext.ConferenceParticipants
            .FirstOrDefaultAsync(p => p.RoomId == roomId && p.UserId == userId && p.LeftAt == null);

        if (participant != null)
        {
            participant.HandRaised = isRaised;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task SetParticipantRoleAsync(long roomId, long userId, ParticipantRole role)
    {
        var participant = await _dbContext.ConferenceParticipants
            .FirstOrDefaultAsync(p => p.RoomId == roomId && p.UserId == userId && p.LeftAt == null);

        if (participant != null)
        {
            participant.Role = role;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<List<ParticipantDto>> GetParticipantsAsync(long roomId)
    {
        var participants = await _dbContext.ConferenceParticipants
            .Where(p => p.RoomId == roomId && p.LeftAt == null)
            .ToListAsync();

        return participants.Select(p => new ParticipantDto
        {
            UserId = p.UserId,
            Role = p.Role,
            VideoEnabled = p.VideoEnabled,
            AudioEnabled = p.AudioEnabled,
            IsScreenSharing = p.IsScreenSharing,
            HandRaised = p.HandRaised,
            JoinedAt = p.JoinedAt
        }).ToList();
    }

    #endregion

    #region 1-on-1 Calls

    public async Task<CallSession> StartCallAsync(StartCallRequest request, long callerId)
    {
        // Check for existing active call in chat
        var existingCall = await _dbContext.CallSessions
            .FirstOrDefaultAsync(c => c.ChatId == request.ChatId && 
                                     (c.Status == CallStatus.Ringing || c.Status == CallStatus.Answered));

        if (existingCall != null)
            throw new InvalidOperationException("There is already an active call in this chat");

        var call = new CallSession
        {
            CallerId = callerId,
            CalleeId = request.CalleeId,
            ChatId = request.ChatId,
            Type = request.Type,
            Status = CallStatus.Ringing,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.CallSessions.Add(call);
        await _dbContext.SaveChangesAsync();

        // Cache call in Redis for quick lookup
        await _redisDb.StringSetAsync($"call:chat:{request.ChatId}", call.Id, TimeSpan.FromMinutes(30));

        _logger.LogInformation("Call {CallId} started: {CallerId} -> {CalleeId}", call.Id, callerId, request.CalleeId);

        return call;
    }

    public async Task<CallSession> AnswerCallAsync(long callId, long calleeId, bool accept)
    {
        var call = await _dbContext.CallSessions.FindAsync(callId);
        if (call == null)
            throw new InvalidOperationException("Call not found");

        if (call.CalleeId != calleeId)
            throw new InvalidOperationException("Only the callee can answer this call");

        if (call.Status != CallStatus.Ringing)
            throw new InvalidOperationException("Call is not in ringing state");

        if (accept)
        {
            call.Status = CallStatus.Answered;
            call.StartedAt = DateTime.UtcNow;
        }
        else
        {
            call.Status = CallStatus.Declined;
            call.EndedAt = DateTime.UtcNow;
            await _redisDb.KeyDeleteAsync($"call:chat:{call.ChatId}");
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Call {CallId} {Action}", callId, accept ? "accepted" : "declined");

        return call;
    }

    public async Task EndCallAsync(long callId, long userId, CallEndReason reason = CallEndReason.UserHangup)
    {
        var call = await _dbContext.CallSessions.FindAsync(callId);
        if (call == null)
            throw new InvalidOperationException("Call not found");

        if (call.CallerId != userId && call.CalleeId != userId)
            throw new InvalidOperationException("Only call participants can end the call");

        if (call.Status == CallStatus.Ended)
            return;

        call.Status = CallStatus.Ended;
        call.EndedAt = DateTime.UtcNow;
        call.EndReason = reason;

        if (call.StartedAt.HasValue)
        {
            call.DurationSeconds = (int)(call.EndedAt.Value - call.StartedAt.Value).TotalSeconds;
        }

        await _redisDb.KeyDeleteAsync($"call:chat:{call.ChatId}");
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Call {CallId} ended by {UserId}, reason: {Reason}", callId, userId, reason);
    }

    public async Task<CallSession?> GetActiveCallAsync(long chatId)
    {
        var callIdStr = await _redisDb.StringGetAsync($"call:chat:{chatId}");
        if (!callIdStr.HasValue)
            return null;

        var callId = long.Parse(callIdStr!);
        return await _dbContext.CallSessions.FindAsync(callId);
    }

    public async Task<CallSession?> GetCallAsync(long callId)
    {
        return await _dbContext.CallSessions.FindAsync(callId);
    }

    #endregion

    #region ICE Servers

    public async Task<List<IceServerDto>> GetIceServersAsync()
    {
        var servers = await _dbContext.IceServers
            .Where(s => s.IsActive)
            .OrderBy(s => s.Priority)
            .ToListAsync();

        return servers.Select(s => new IceServerDto
        {
            Url = s.Url,
            Username = s.Username,
            Credential = s.Credential
        }).ToList();
    }

    public async Task<WebRtcConfig> GetWebRtcConfigAsync()
    {
        var servers = await GetIceServersAsync();
        
        return new WebRtcConfig
        {
            IceServers = servers,
            IceTransportPolicy = 0 // All interfaces
        };
    }

    #endregion

    #region Helper Methods

    private static string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = RandomNumberGenerator.Create();
        var result = new char[8];
        var buffer = new byte[1];

        for (int i = 0; i < result.Length; i++)
        {
            random.GetBytes(buffer);
            result[i] = chars[buffer[0] % chars.Length];
        }

        return new string(result);
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    private static bool VerifyPassword(string password, string hash)
    {
        return HashPassword(password) == hash;
    }

    private async Task<RoomDto> MapToRoomDto(ConferenceRoom room)
    {
        var participants = room.Participants
            .Where(p => p.LeftAt == null)
            .Select(p => new ParticipantDto
            {
                UserId = p.UserId,
                Role = p.Role,
                VideoEnabled = p.VideoEnabled,
                AudioEnabled = p.AudioEnabled,
                IsScreenSharing = p.IsScreenSharing,
                HandRaised = p.HandRaised,
                JoinedAt = p.JoinedAt
            }).ToList();

        return new RoomDto
        {
            Id = room.Id,
            RoomCode = room.RoomCode,
            Title = room.Title,
            ChatId = room.ChatId,
            CreatorId = room.CreatorId,
            Type = room.Type,
            MaxParticipants = room.MaxParticipants,
            ParticipantCount = room.ParticipantCount,
            Status = room.Status,
            HasPassword = room.HasPassword,
            VideoEnabled = room.VideoEnabled,
            AudioEnabled = room.AudioEnabled,
            ScreenShareEnabled = room.ScreenShareEnabled,
            StartTime = room.StartTime,
            EndTime = room.EndTime,
            Participants = participants
        };
    }

    #endregion
}
