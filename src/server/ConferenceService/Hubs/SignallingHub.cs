using ConferenceService.Models;
using ConferenceService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Text.Json;

namespace ConferenceService.Hubs;

/// <summary>
/// SignalR hub for WebRTC signalling
/// </summary>
[Authorize]
public class SignallingHub : Hub
{
    private readonly IConferenceService _conferenceService;
    private readonly ILogger<SignallingHub> _logger;
    private static readonly Dictionary<string, long> _connectionToUser = new();
    private static readonly Dictionary<long, HashSet<string>> _userConnections = new();
    private static readonly Dictionary<long, long> _userInRoom = new();

    public SignallingHub(
        IConferenceService conferenceService,
        ILogger<SignallingHub> logger)
    {
        _conferenceService = conferenceService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        var connectionId = Context.ConnectionId;

        _connectionToUser[connectionId] = userId;
        
        if (!_userConnections.ContainsKey(userId))
            _userConnections[userId] = new HashSet<string>();
        
        _userConnections[userId].Add(connectionId);

        _logger.LogInformation("User {UserId} connected with connection {ConnectionId}", userId, connectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;
        
        if (_connectionToUser.TryGetValue(connectionId, out var userId))
        {
            _connectionToUser.Remove(connectionId);
            
            if (_userConnections.TryGetValue(userId, out var connections))
            {
                connections.Remove(connectionId);
                if (connections.Count == 0)
                    _userConnections.Remove(userId);
            }

            // Leave room if user was in one
            if (_userInRoom.TryGetValue(userId, out var roomId))
            {
                await HandleLeaveRoom(roomId, userId);
                _userInRoom.Remove(userId);
            }

            _logger.LogInformation("User {UserId} disconnected", userId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    #region Room Operations

    /// <summary>
    /// Join a conference room
    /// </summary>
    public async Task JoinRoom(long roomId, string? password = null)
    {
        var userId = GetUserId();
        
        try
        {
            var room = await _conferenceService.JoinRoomAsync(roomId, userId, password);
            
            // Add to SignalR group
            await Groups.AddToGroupAsync(Context.ConnectionId, $"room:{roomId}");
            _userInRoom[userId] = roomId;

            // Notify others in the room
            await Clients.OthersInGroup($"room:{roomId}").SendAsync("UserJoined", new
            {
                UserId = userId,
                RoomId = roomId,
                Timestamp = DateTime.UtcNow
            });

            // Send room info to the joiner
            await Clients.Caller.SendAsync("RoomJoined", room);

            _logger.LogInformation("User {UserId} joined room {RoomId} via SignalR", userId, roomId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join room {RoomId}", roomId);
            await Clients.Caller.SendAsync("Error", new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Leave a conference room
    /// </summary>
    public async Task LeaveRoom(long roomId)
    {
        var userId = GetUserId();
        await HandleLeaveRoom(roomId, userId);
    }

    private async Task HandleLeaveRoom(long roomId, long userId)
    {
        try
        {
            await _conferenceService.LeaveRoomAsync(roomId, userId);
            
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room:{roomId}");
            _userInRoom.Remove(userId);

            // Notify others in the room
            await Clients.OthersInGroup($"room:{roomId}").SendAsync("UserLeft", new
            {
                UserId = userId,
                RoomId = roomId,
                Timestamp = DateTime.UtcNow
            });

            _logger.LogInformation("User {UserId} left room {RoomId} via SignalR", userId, roomId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to leave room {RoomId}", roomId);
        }
    }

    #endregion

    #region WebRTC Signalling

    /// <summary>
    /// Send SDP offer to a specific peer or broadcast to room
    /// </summary>
    public async Task SendOffer(long roomId, long? targetUserId, string sdp)
    {
        var userId = GetUserId();
        
        var message = new
        {
            FromUserId = userId,
            RoomId = roomId,
            SDP = sdp,
            Timestamp = DateTime.UtcNow
        };

        if (targetUserId.HasValue)
        {
            // Send to specific user
            await SendToUser(targetUserId.Value, "Offer", message);
        }
        else
        {
            // Broadcast to room (except sender)
            await Clients.OthersInGroup($"room:{roomId}").SendAsync("Offer", message);
        }

        _logger.LogDebug("Offer sent from {UserId} to {Target} in room {RoomId}", 
            userId, targetUserId?.ToString() ?? "all", roomId);
    }

    /// <summary>
    /// Send SDP answer to a specific peer
    /// </summary>
    public async Task SendAnswer(long roomId, long targetUserId, string sdp)
    {
        var userId = GetUserId();
        
        var message = new
        {
            FromUserId = userId,
            RoomId = roomId,
            SDP = sdp,
            Timestamp = DateTime.UtcNow
        };

        await SendToUser(targetUserId, "Answer", message);

        _logger.LogDebug("Answer sent from {UserId} to {TargetUserId} in room {RoomId}", 
            userId, targetUserId, roomId);
    }

    /// <summary>
    /// Send ICE candidate to a specific peer or broadcast to room
    /// </summary>
    public async Task SendIceCandidate(long roomId, long? targetUserId, string candidate)
    {
        var userId = GetUserId();
        
        var message = new
        {
            FromUserId = userId,
            RoomId = roomId,
            Candidate = candidate,
            Timestamp = DateTime.UtcNow
        };

        if (targetUserId.HasValue)
        {
            await SendToUser(targetUserId.Value, "IceCandidate", message);
        }
        else
        {
            await Clients.OthersInGroup($"room:{roomId}").SendAsync("IceCandidate", message);
        }

        _logger.LogDebug("ICE candidate sent from {UserId} to {Target} in room {RoomId}", 
            userId, targetUserId?.ToString() ?? "all", roomId);
    }

    #endregion

    #region Media Control

    /// <summary>
    /// Toggle video on/off
    /// </summary>
    public async Task ToggleVideo(long roomId, bool enabled)
    {
        var userId = GetUserId();
        
        await _conferenceService.UpdateParticipantMediaAsync(roomId, userId, videoEnabled: enabled);
        
        await Clients.OthersInGroup($"room:{roomId}").SendAsync("VideoStateChanged", new
        {
            UserId = userId,
            VideoEnabled = enabled,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Toggle audio (mute/unmute)
    /// </summary>
    public async Task ToggleAudio(long roomId, bool enabled)
    {
        var userId = GetUserId();
        
        await _conferenceService.UpdateParticipantMediaAsync(roomId, userId, audioEnabled: enabled);
        
        await Clients.OthersInGroup($"room:{roomId}").SendAsync("AudioStateChanged", new
        {
            UserId = userId,
            AudioEnabled = enabled,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Start screen sharing
    /// </summary>
    public async Task StartScreenShare(long roomId)
    {
        var userId = GetUserId();
        
        await _conferenceService.SetScreenSharingAsync(roomId, userId, true);
        
        await Clients.OthersInGroup($"room:{roomId}").SendAsync("ScreenShareStarted", new
        {
            UserId = userId,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Stop screen sharing
    /// </summary>
    public async Task StopScreenShare(long roomId)
    {
        var userId = GetUserId();
        
        await _conferenceService.SetScreenSharingAsync(roomId, userId, false);
        
        await Clients.OthersInGroup($"room:{roomId}").SendAsync("ScreenShareStopped", new
        {
            UserId = userId,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Raise hand
    /// </summary>
    public async Task RaiseHand(long roomId)
    {
        var userId = GetUserId();
        
        await _conferenceService.SetHandRaisedAsync(roomId, userId, true);
        
        await Clients.Group($"room:{roomId}").SendAsync("HandRaised", new
        {
            UserId = userId,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Lower hand
    /// </summary>
    public async Task LowerHand(long roomId)
    {
        var userId = GetUserId();
        
        await _conferenceService.SetHandRaisedAsync(roomId, userId, false);
        
        await Clients.Group($"room:{roomId}").SendAsync("HandLowered", new
        {
            UserId = userId,
            Timestamp = DateTime.UtcNow
        });
    }

    #endregion

    #region 1-on-1 Calls

    /// <summary>
    /// Initiate a 1-on-1 call
    /// </summary>
    public async Task StartCall(long calleeId, long chatId, CallType callType)
    {
        var callerId = GetUserId();
        
        try
        {
            var request = new StartCallRequest
            {
                CalleeId = calleeId,
                ChatId = chatId,
                Type = callType
            };

            var call = await _conferenceService.StartCallAsync(request, callerId);

            // Send call notification to callee
            await SendToUser(calleeId, "IncomingCall", new
            {
                CallId = call.Id,
                CallerId = callerId,
                ChatId = chatId,
                Type = callType,
                Timestamp = DateTime.UtcNow
            });

            // Get WebRTC config for caller
            var config = await _conferenceService.GetWebRtcConfigAsync();
            await Clients.Caller.SendAsync("CallStarted", new
            {
                CallId = call.Id,
                CalleeId = calleeId,
                Config = config,
                Timestamp = DateTime.UtcNow
            });

            _logger.LogInformation("Call {CallId} initiated: {CallerId} -> {CalleeId}", call.Id, callerId, calleeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start call");
            await Clients.Caller.SendAsync("Error", new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Answer an incoming call
    /// </summary>
    public async Task AnswerCall(long callId, bool accept)
    {
        var userId = GetUserId();
        
        try
        {
            var call = await _conferenceService.AnswerCallAsync(callId, userId, accept);

            // Notify caller about the answer
            await SendToUser(call.CallerId, "CallAnswered", new
            {
                CallId = callId,
                CalleeId = userId,
                Accepted = accept,
                Timestamp = DateTime.UtcNow
            });

            if (accept)
            {
                // Both parties join a SignalR group for this call
                var callGroup = $"call:{callId}";
                await Groups.AddToGroupAsync(Context.ConnectionId, callGroup);
                
                // Get WebRTC config for callee
                var config = await _conferenceService.GetWebRtcConfigAsync();
                await Clients.Caller.SendAsync("CallConfig", new
                {
                    CallId = callId,
                    Config = config
                });
            }

            _logger.LogInformation("Call {CallId} {Action}", callId, accept ? "accepted" : "declined");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to answer call {CallId}", callId);
            await Clients.Caller.SendAsync("Error", new { Message = ex.Message });
        }
    }

    /// <summary>
    /// End a call
    /// </summary>
    public async Task EndCall(long callId)
    {
        var userId = GetUserId();
        
        try
        {
            var call = await _conferenceService.GetCallAsync(callId);
            if (call == null)
                return;

            await _conferenceService.EndCallAsync(callId, userId);

            // Notify the other party
            var otherUserId = userId == call.CallerId ? call.CalleeId : call.CallerId;
            await SendToUser(otherUserId, "CallEnded", new
            {
                CallId = callId,
                EndedBy = userId,
                Timestamp = DateTime.UtcNow
            });

            _logger.LogInformation("Call {CallId} ended by {UserId}", callId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to end call {CallId}", callId);
        }
    }

    #endregion

    #region Helper Methods

    private long GetUserId()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(userIdClaim, out var userId) ? userId : 0;
    }

    private async Task SendToUser(long userId, string method, object? arg)
    {
        if (_userConnections.TryGetValue(userId, out var connections))
        {
            foreach (var connectionId in connections)
            {
                await Clients.Client(connectionId).SendAsync(method, arg);
            }
        }
    }

    #endregion
}
