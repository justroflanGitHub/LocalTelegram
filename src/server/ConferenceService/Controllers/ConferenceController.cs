using ConferenceService.Models;
using ConferenceService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ConferenceService.Controllers;

[ApiController]
[Route("api/conference")]
[Authorize]
public class ConferenceController : ControllerBase
{
    private readonly IConferenceService _conferenceService;
    private readonly ILogger<ConferenceController> _logger;

    public ConferenceController(
        IConferenceService conferenceService,
        ILogger<ConferenceController> logger)
    {
        _conferenceService = conferenceService;
        _logger = logger;
    }

    #region Room Management

    /// <summary>
    /// Create a new conference room
    /// </summary>
    [HttpPost("rooms")]
    public async Task<ActionResult<RoomDto>> CreateRoom([FromBody] CreateRoomRequest request)
    {
        try
        {
            var userId = GetUserId();
            var room = await _conferenceService.CreateRoomAsync(request, userId);
            return Ok(await MapToRoomDto(room));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create room");
            return StatusCode(500, new { error = "Failed to create room" });
        }
    }

    /// <summary>
    /// Get room by ID
    /// </summary>
    [HttpGet("rooms/{roomId}")]
    public async Task<ActionResult<RoomDto>> GetRoom(long roomId)
    {
        var room = await _conferenceService.GetRoomAsync(roomId);
        if (room == null)
            return NotFound(new { error = "Room not found" });

        return Ok(await MapToRoomDto(room));
    }

    /// <summary>
    /// Get room by code
    /// </summary>
    [HttpGet("rooms/code/{roomCode}")]
    public async Task<ActionResult<RoomDto>> GetRoomByCode(string roomCode)
    {
        var room = await _conferenceService.GetRoomByCodeAsync(roomCode);
        if (room == null)
            return NotFound(new { error = "Room not found" });

        return Ok(await MapToRoomDto(room));
    }

    /// <summary>
    /// Join a conference room
    /// </summary>
    [HttpPost("rooms/{roomId}/join")]
    public async Task<ActionResult<RoomDto>> JoinRoom(long roomId, [FromBody] JoinRoomRequest? request = null)
    {
        try
        {
            var userId = GetUserId();
            var room = await _conferenceService.JoinRoomAsync(roomId, userId, request?.Password);
            return Ok(room);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Leave a conference room
    /// </summary>
    [HttpPost("rooms/{roomId}/leave")]
    public async Task<ActionResult> LeaveRoom(long roomId)
    {
        var userId = GetUserId();
        await _conferenceService.LeaveRoomAsync(roomId, userId);
        return Ok(new { success = true });
    }

    /// <summary>
    /// End a conference room (creator only)
    /// </summary>
    [HttpPost("rooms/{roomId}/end")]
    public async Task<ActionResult> EndRoom(long roomId)
    {
        try
        {
            var userId = GetUserId();
            await _conferenceService.EndRoomAsync(roomId, userId);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get active rooms for a chat/group
    /// </summary>
    [HttpGet("rooms/active")]
    public async Task<ActionResult<List<RoomDto>>> GetActiveRooms([FromQuery] long? chatId = null)
    {
        var rooms = await _conferenceService.GetActiveRoomsAsync(chatId);
        return Ok(rooms);
    }

    /// <summary>
    /// Get room participants
    /// </summary>
    [HttpGet("rooms/{roomId}/participants")]
    public async Task<ActionResult<List<ParticipantDto>>> GetParticipants(long roomId)
    {
        var participants = await _conferenceService.GetParticipantsAsync(roomId);
        return Ok(participants);
    }

    #endregion

    #region Participant Management

    /// <summary>
    /// Update participant media state
    /// </summary>
    [HttpPut("rooms/{roomId}/media")]
    public async Task<ActionResult> UpdateMedia(long roomId, [FromBody] UpdateMediaRequest request)
    {
        try
        {
            var userId = GetUserId();
            await _conferenceService.UpdateParticipantMediaAsync(roomId, userId, request.VideoEnabled, request.AudioEnabled);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Set screen sharing state
    /// </summary>
    [HttpPost("rooms/{roomId}/screen-share")]
    public async Task<ActionResult> SetScreenShare(long roomId, [FromBody] ScreenShareRequest request)
    {
        var userId = GetUserId();
        await _conferenceService.SetScreenSharingAsync(roomId, userId, request.IsSharing);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Set hand raised state
    /// </summary>
    [HttpPost("rooms/{roomId}/hand")]
    public async Task<ActionResult> SetHandRaised(long roomId, [FromBody] HandRequest request)
    {
        var userId = GetUserId();
        await _conferenceService.SetHandRaisedAsync(roomId, userId, request.IsRaised);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Set participant role (admin/moderator only)
    /// </summary>
    [HttpPut("rooms/{roomId}/participants/{userId}/role")]
    public async Task<ActionResult> SetParticipantRole(long roomId, long userId, [FromBody] SetRoleRequest request)
    {
        try
        {
            await _conferenceService.SetParticipantRoleAsync(roomId, userId, request.Role);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    #endregion

    #region 1-on-1 Calls

    /// <summary>
    /// Start a 1-on-1 call
    /// </summary>
    [HttpPost("calls")]
    public async Task<ActionResult<CallDto>> StartCall([FromBody] StartCallRequest request)
    {
        try
        {
            var userId = GetUserId();
            var call = await _conferenceService.StartCallAsync(request, userId);
            return Ok(MapToCallDto(call));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Answer a call
    /// </summary>
    [HttpPost("calls/{callId}/answer")]
    public async Task<ActionResult<CallDto>> AnswerCall(long callId, [FromBody] AnswerCallRequest request)
    {
        try
        {
            var userId = GetUserId();
            var call = await _conferenceService.AnswerCallAsync(callId, userId, request.Accept);
            return Ok(MapToCallDto(call));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// End a call
    /// </summary>
    [HttpPost("calls/{callId}/end")]
    public async Task<ActionResult> EndCall(long callId, [FromBody] EndCallRequest? request = null)
    {
        try
        {
            var userId = GetUserId();
            await _conferenceService.EndCallAsync(callId, userId, request?.Reason ?? CallEndReason.UserHangup);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get active call for a chat
    /// </summary>
    [HttpGet("calls/active/{chatId}")]
    public async Task<ActionResult<CallDto>> GetActiveCall(long chatId)
    {
        var call = await _conferenceService.GetActiveCallAsync(chatId);
        if (call == null)
            return NotFound(new { error = "No active call" });

        return Ok(MapToCallDto(call));
    }

    /// <summary>
    /// Get call by ID
    /// </summary>
    [HttpGet("calls/{callId}")]
    public async Task<ActionResult<CallDto>> GetCall(long callId)
    {
        var call = await _conferenceService.GetCallAsync(callId);
        if (call == null)
            return NotFound(new { error = "Call not found" });

        return Ok(MapToCallDto(call));
    }

    #endregion

    #region WebRTC Configuration

    /// <summary>
    /// Get ICE servers configuration
    /// </summary>
    [HttpGet("webrtc/ice-servers")]
    [AllowAnonymous] // Allow for client configuration before auth
    public async Task<ActionResult<List<IceServerDto>>> GetIceServers()
    {
        var servers = await _conferenceService.GetIceServersAsync();
        return Ok(servers);
    }

    /// <summary>
    /// Get full WebRTC configuration
    /// </summary>
    [HttpGet("webrtc/config")]
    public async Task<ActionResult<WebRtcConfig>> GetWebRtcConfig()
    {
        var config = await _conferenceService.GetWebRtcConfigAsync();
        return Ok(config);
    }

    #endregion

    #region Helper Methods

    private long GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(userIdClaim, out var userId) ? userId : 0;
    }

    private async Task<RoomDto> MapToRoomDto(ConferenceRoom room)
    {
        var participants = await _conferenceService.GetParticipantsAsync(room.Id);
        
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

    private static CallDto MapToCallDto(CallSession call)
    {
        return new CallDto
        {
            Id = call.Id,
            CallerId = call.CallerId,
            CalleeId = call.CalleeId,
            ChatId = call.ChatId,
            Type = call.Type,
            Status = call.Status,
            StartedAt = call.StartedAt,
            EndedAt = call.EndedAt,
            DurationSeconds = call.DurationSeconds,
            CreatedAt = call.CreatedAt
        };
    }

    #endregion
}

#region Request Models

public class UpdateMediaRequest
{
    public bool? VideoEnabled { get; set; }
    public bool? AudioEnabled { get; set; }
}

public class ScreenShareRequest
{
    public bool IsSharing { get; set; }
}

public class HandRequest
{
    public bool IsRaised { get; set; }
}

public class SetRoleRequest
{
    public ParticipantRole Role { get; set; }
}

#endregion
