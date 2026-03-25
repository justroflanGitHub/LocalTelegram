using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ConferenceService.Models;

/// <summary>
/// Represents a conference room
/// </summary>
public class ConferenceRoom
{
    [Key]
    public long Id { get; set; }
    
    /// <summary>
    /// Unique room identifier for joining
    /// </summary>
    [MaxLength(50)]
    public string RoomCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Room title
    /// </summary>
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;
    
    /// <summary>
    /// Associated chat/group ID (optional)
    /// </summary>
    public long? ChatId { get; set; }
    
    /// <summary>
    /// Room creator
    /// </summary>
    public long CreatorId { get; set; }
    
    /// <summary>
    /// Room type
    /// </summary>
    public ConferenceType Type { get; set; } = ConferenceType.Video;
    
    /// <summary>
    /// Maximum participants
    /// </summary>
    public int MaxParticipants { get; set; } = 100;
    
    /// <summary>
    /// Current participant count
    /// </summary>
    public int ParticipantCount { get; set; }
    
    /// <summary>
    /// Room status
    /// </summary>
    public ConferenceStatus Status { get; set; } = ConferenceStatus.Waiting;
    
    /// <summary>
    /// Is room password protected
    /// </summary>
    public bool HasPassword { get; set; }
    
    /// <summary>
    /// Hashed password (if protected)
    /// </summary>
    [MaxLength(200)]
    public string? PasswordHash { get; set; }
    
    /// <summary>
    /// Enable video
    /// </summary>
    public bool VideoEnabled { get; set; } = true;
    
    /// <summary>
    /// Enable audio
    /// </summary>
    public bool AudioEnabled { get; set; } = true;
    
    /// <summary>
    /// Enable screen sharing
    /// </summary>
    public bool ScreenShareEnabled { get; set; } = true;
    
    /// <summary>
    /// Enable recording
    /// </summary>
    public bool RecordingEnabled { get; set; } = false;
    
    /// <summary>
    /// Recording started at
    /// </summary>
    public DateTime? RecordingStartedAt { get; set; }
    
    /// <summary>
    /// Room start time
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Room end time (null if ongoing)
    /// </summary>
    public DateTime? EndTime { get; set; }
    
    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Navigation to participants
    /// </summary>
    [JsonIgnore]
    public ICollection<ConferenceParticipant> Participants { get; set; } = new List<ConferenceParticipant>();
}

/// <summary>
/// Represents a participant in a conference
/// </summary>
public class ConferenceParticipant
{
    [Key]
    public long Id { get; set; }
    
    /// <summary>
    /// Room ID
    /// </summary>
    public long RoomId { get; set; }
    
    /// <summary>
    /// User ID
    /// </summary>
    public long UserId { get; set; }
    
    /// <summary>
    /// Participant role
    /// </summary>
    public ParticipantRole Role { get; set; } = ParticipantRole.Member;
    
    /// <summary>
    /// Is video enabled
    /// </summary>
    public bool VideoEnabled { get; set; }
    
    /// <summary>
    /// Is audio enabled (muted/unmuted)
    /// </summary>
    public bool AudioEnabled { get; set; }
    
    /// <summary>
    /// Is screen sharing
    /// </summary>
    public bool IsScreenSharing { get; set; }
    
    /// <summary>
    /// Is hand raised
    /// </summary>
    public bool HandRaised { get; set; }
    
    /// <summary>
    /// Join time
    /// </summary>
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Leave time (null if still in room)
    /// </summary>
    public DateTime? LeftAt { get; set; }
    
    /// <summary>
    /// Connection ID for SignalR
    /// </summary>
    [MaxLength(100)]
    public string? ConnectionId { get; set; }
    
    /// <summary>
    /// Navigation to room
    /// </summary>
    [JsonIgnore]
    public ConferenceRoom Room { get; set; } = null!;
}

/// <summary>
/// Represents a call session (1-on-1)
/// </summary>
public class CallSession
{
    [Key]
    public long Id { get; set; }
    
    /// <summary>
    /// Caller user ID
    /// </summary>
    public long CallerId { get; set; }
    
    /// <summary>
    /// Callee user ID
    /// </summary>
    public long CalleeId { get; set; }
    
    /// <summary>
    /// Chat ID where call was initiated
    /// </summary>
    public long ChatId { get; set; }
    
    /// <summary>
    /// Call type
    /// </summary>
    public CallType Type { get; set; }
    
    /// <summary>
    /// Call status
    /// </summary>
    public CallStatus Status { get; set; } = CallStatus.Ringing;
    
    /// <summary>
    /// Call started at (when answered)
    /// </summary>
    public DateTime? StartedAt { get; set; }
    
    /// <summary>
    /// Call ended at
    /// </summary>
    public DateTime? EndedAt { get; set; }
    
    /// <summary>
    /// Call duration in seconds
    /// </summary>
    public int? DurationSeconds { get; set; }
    
    /// <summary>
    /// End reason
    /// </summary>
    public CallEndReason? EndReason { get; set; }
    
    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// ICE server configuration for WebRTC
/// </summary>
public class IceServer
{
    [Key]
    public long Id { get; set; }
    
    /// <summary>
    /// Server URL (stun: or turn:)
    /// </summary>
    [MaxLength(500)]
    public string Url { get; set; } = string.Empty;
    
    /// <summary>
    /// Server type
    /// </summary>
    public IceServerType Type { get; set; }
    
    /// <summary>
    /// Username for TURN authentication
    /// </summary>
    [MaxLength(100)]
    public string? Username { get; set; }
    
    /// <summary>
    /// Credential for TURN authentication
    /// </summary>
    [MaxLength(200)]
    public string? Credential { get; set; }
    
    /// <summary>
    /// Is server active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Priority (lower = higher priority)
    /// </summary>
    public int Priority { get; set; } = 100;
}

/// <summary>
/// Signalling message for WebRTC negotiation
/// </summary>
public class SignallingMessage
{
    public long Id { get; set; }
    public long RoomId { get; set; }
    public long FromUserId { get; set; }
    public long? ToUserId { get; set; } // null for broadcast
    public SignallingMessageType Type { get; set; }
    public string Payload { get; set; } = string.Empty; // JSON encoded SDP or ICE candidate
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

#region Enums

public enum ConferenceType
{
    Audio = 0,
    Video = 1,
    AudioGroup = 2,
    VideoGroup = 3
}

public enum ConferenceStatus
{
    Waiting = 0,
    Active = 1,
    Ended = 2,
    Cancelled = 3
}

public enum ParticipantRole
{
    Member = 0,
    Moderator = 1,
    Admin = 2,
    Presenter = 3
}

public enum CallType
{
    Audio = 0,
    Video = 1
}

public enum CallStatus
{
    Ringing = 0,
    Answered = 1,
    Declined = 2,
    Missed = 3,
    Ended = 4,
    Busy = 5
}

public enum CallEndReason
{
    Normal = 0,
    UserHangup = 1,
    ConnectionLost = 2,
    Timeout = 3,
    Error = 4
}

public enum IceServerType
{
    Stun = 0,
    Turn = 1
}

public enum SignallingMessageType
{
    Offer = 0,
    Answer = 1,
    IceCandidate = 2,
    Join = 3,
    Leave = 4,
    Mute = 5,
    Unmute = 6,
    VideoOn = 7,
    VideoOff = 8,
    ScreenShareStart = 9,
    ScreenShareStop = 10,
    RaiseHand = 11,
    LowerHand = 12
}

#endregion

#region DTOs

public class CreateRoomRequest
{
    public string Title { get; set; } = string.Empty;
    public long? ChatId { get; set; }
    public ConferenceType Type { get; set; } = ConferenceType.Video;
    public int MaxParticipants { get; set; } = 100;
    public string? Password { get; set; }
    public bool VideoEnabled { get; set; } = true;
    public bool AudioEnabled { get; set; } = true;
    public bool ScreenShareEnabled { get; set; } = true;
    public bool RecordingEnabled { get; set; } = false;
}

public class JoinRoomRequest
{
    public string RoomCode { get; set; } = string.Empty;
    public string? Password { get; set; }
}

public class StartCallRequest
{
    public long CalleeId { get; set; }
    public long ChatId { get; set; }
    public CallType Type { get; set; }
}

public class AnswerCallRequest
{
    public long CallId { get; set; }
    public bool Accept { get; set; }
}

public class EndCallRequest
{
    public long CallId { get; set; }
    public CallEndReason Reason { get; set; } = CallEndReason.UserHangup;
}

public class RoomDto
{
    public long Id { get; set; }
    public string RoomCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long? ChatId { get; set; }
    public long CreatorId { get; set; }
    public ConferenceType Type { get; set; }
    public int MaxParticipants { get; set; }
    public int ParticipantCount { get; set; }
    public ConferenceStatus Status { get; set; }
    public bool HasPassword { get; set; }
    public bool VideoEnabled { get; set; }
    public bool AudioEnabled { get; set; }
    public bool ScreenShareEnabled { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public List<ParticipantDto> Participants { get; set; } = new();
}

public class ParticipantDto
{
    public long UserId { get; set; }
    public ParticipantRole Role { get; set; }
    public bool VideoEnabled { get; set; }
    public bool AudioEnabled { get; set; }
    public bool IsScreenSharing { get; set; }
    public bool HandRaised { get; set; }
    public DateTime JoinedAt { get; set; }
}

public class CallDto
{
    public long Id { get; set; }
    public long CallerId { get; set; }
    public long CalleeId { get; set; }
    public long ChatId { get; set; }
    public CallType Type { get; set; }
    public CallStatus Status { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class IceServerDto
{
    public string Url { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Credential { get; set; }
}

public class WebRtcConfig
{
    public List<IceServerDto> IceServers { get; set; } = new();
    public int IceTransportPolicy { get; set; } = 0; // 0 = all, 1 = relay
}

#endregion
