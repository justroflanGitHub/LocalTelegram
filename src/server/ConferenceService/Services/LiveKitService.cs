using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using LiveKit.Server;
using ConferenceService.Models;

namespace ConferenceService.Services;

/// <summary>
/// LiveKit SFU integration service for video conferencing
/// </summary>
public interface ILiveKitService
{
    /// <summary>
    /// Creates a new LiveKit room
    /// </summary>
    Task<LiveKitRoom> CreateRoomAsync(CreateRoomRequest request, long creatorId);
    
    /// <summary>
    /// Gets room information
    /// </summary>
    Task<LiveKitRoom?> GetRoomAsync(string roomName);
    
    /// <summary>
    /// Lists all active rooms
    /// </summary>
    Task<IEnumerable<LiveKitRoom>> ListRoomsAsync();
    
    /// <summary>
    /// Deletes a room
    /// </summary>
    Task<bool> DeleteRoomAsync(string roomName);
    
    /// <summary>
    /// Generates access token for a participant
    /// </summary>
    string GenerateAccessToken(long userId, string roomName, string displayName, bool isModerator = false);
    
    /// <summary>
    /// Gets participants in a room
    /// </summary>
    Task<IEnumerable<LiveKitParticipant>> GetParticipantsAsync(string roomName);
    
    /// <summary>
    /// Removes a participant from a room
    /// </summary>
    Task<bool> RemoveParticipantAsync(string roomName, string participantIdentity);
    
    /// <summary>
    /// Mutes/unmutes a participant's track
    /// </summary>
    Task<bool> MuteTrackAsync(string roomName, string participantIdentity, string trackSid, bool muted);
    
    /// <summary>
    /// Starts recording a room
    /// </summary>
    Task<RecordingInfo> StartRecordingAsync(string roomName, RecordingOptions? options = null);
    
    /// <summary>
    /// Stops recording
    /// </summary>
    Task<bool> StopRecordingAsync(string recordingId);
    
    /// <summary>
    /// Gets Egress service client for recording/streaming
    /// </summary>
    Task<IEnumerable<RecordingInfo>> ListRecordingsAsync(string? roomName = null);
}

/// <summary>
/// LiveKit room information
/// </summary>
public class LiveKitRoom
{
    public string Name { get; set; } = string.Empty;
    public string Sid { get; set; } = string.Empty;
    public int NumParticipants { get; set; }
    public int NumPublishers { get; set; }
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// LiveKit participant information
/// </summary>
public class LiveKitParticipant
{
    public string Identity { get; set; } = string.Empty;
    public string Sid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; }
    public List<TrackInfo> Tracks { get; set; } = new();
}

/// <summary>
/// Track information
/// </summary>
public class TrackInfo
{
    public string Sid { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // audio, video
    public string Name { get; set; } = string.Empty;
    public bool Muted { get; set; }
    public string Source { get; set; } = string.Empty; // camera, microphone, screen_share
}

/// <summary>
/// Recording information
/// </summary>
public class RecordingInfo
{
    public string RecordingId { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? FileLocation { get; set; }
}

/// <summary>
/// Recording options
/// </summary>
public class RecordingOptions
{
    public string? FilePrefix { get; set; }
    public string Format { get; set; } = "mp4";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int VideoBitrate { get; set; } = 2500;
    public int AudioBitrate { get; set; } = 128;
}

/// <summary>
/// LiveKit configuration options
/// </summary>
public class LiveKitOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string Host { get; set; } = "http://localhost:7880";
    public string WsUrl { get; set; } = "ws://localhost:7880";
}

/// <summary>
/// LiveKit SFU service implementation
/// </summary>
public class LiveKitService : ILiveKitService
{
    private readonly LiveKitOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<LiveKitService> _logger;
    private readonly RoomServiceClient _roomService;
    private readonly AccessTokenFactory _tokenFactory;

    public LiveKitService(
        IOptions<LiveKitOptions> options,
        HttpClient httpClient,
        ILogger<LiveKitService> logger)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _logger = logger;
        
        // Initialize LiveKit SDK clients
        _roomService = new RoomServiceClient(_options.Host, _options.ApiKey, _options.ApiSecret);
        _tokenFactory = new AccessTokenFactory(_options.ApiKey, _options.ApiSecret);
    }

    /// <inheritdoc/>
    public async Task<LiveKitRoom> CreateRoomAsync(CreateRoomRequest request, long creatorId)
    {
        try
        {
            var roomName = $"room-{Guid.NewGuid():N}";
            
            // Create room via LiveKit API
            var createRequest = new CreateRoomRequest
            {
                Name = roomName,
                EmptyTimeout = request.EmptyTimeout > 0 ? request.EmptyTimeout : 300, // 5 minutes
                MaxParticipants = request.MaxParticipants > 0 ? request.MaxParticipants : 100,
                Metadata = JsonSerializer.Serialize(new
                {
                    CreatorId = creatorId,
                    Title = request.Title,
                    Type = request.Type.ToString(),
                    CreatedAt = DateTime.UtcNow
                })
            };

            var room = await _roomService.CreateRoom(createRequest);
            
            _logger.LogInformation("Created LiveKit room {RoomName} for creator {CreatorId}", 
                roomName, creatorId);

            return new LiveKitRoom
            {
                Name = room.Name,
                Sid = room.Sid,
                NumParticipants = room.NumParticipants,
                NumPublishers = room.NumPublishers,
                CreatedAt = room.CreationTime.ToDateTime(),
                Metadata = new Dictionary<string, string>
                {
                    ["creatorId"] = creatorId.ToString(),
                    ["title"] = request.Title ?? string.Empty
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create LiveKit room");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<LiveKitRoom?> GetRoomAsync(string roomName)
    {
        try
        {
            var room = await _roomService.GetRoom(new GetRoomRequest { Room = roomName });
            
            if (room == null)
                return null;

            return new LiveKitRoom
            {
                Name = room.Name,
                Sid = room.Sid,
                NumParticipants = room.NumParticipants,
                NumPublishers = room.NumPublishers,
                CreatedAt = room.CreationTime.ToDateTime(),
                Metadata = room.Metadata != null 
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(room.Metadata) ?? new()
                    : new()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get room {RoomName}", roomName);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<LiveKitRoom>> ListRoomsAsync()
    {
        try
        {
            var response = await _roomService.ListRooms(new ListRoomsRequest());
            
            return response.Rooms.Select(room => new LiveKitRoom
            {
                Name = room.Name,
                Sid = room.Sid,
                NumParticipants = room.NumParticipants,
                NumPublishers = room.NumPublishers,
                CreatedAt = room.CreationTime.ToDateTime(),
                Metadata = room.Metadata != null
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(room.Metadata) ?? new()
                    : new()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list rooms");
            return Enumerable.Empty<LiveKitRoom>();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteRoomAsync(string roomName)
    {
        try
        {
            await _roomService.DeleteRoom(new DeleteRoomRequest { Room = roomName });
            
            _logger.LogInformation("Deleted LiveKit room {RoomName}", roomName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete room {RoomName}", roomName);
            return false;
        }
    }

    /// <inheritdoc/>
    public string GenerateAccessToken(long userId, string roomName, string displayName, bool isModerator = false)
    {
        try
        {
            var token = _tokenFactory.CreateAccessToken(
                identity: userId.ToString(),
                name: displayName
            );

            // Set room permissions
            var grant = new VideoGrant
            {
                Room = roomName,
                RoomJoin = true,
                RoomCreate = isModerator,
                RoomAdmin = isModerator,
                CanPublish = true,
                CanSubscribe = true,
                CanPublishData = true
            };

            token.SetVideoGrant(grant);
            token.SetTtl(TimeSpan.FromHours(6)); // 6 hour token validity

            var jwt = token.ToJwt();
            
            _logger.LogDebug("Generated access token for user {UserId} in room {RoomName}", 
                userId, roomName);
            
            return jwt;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate access token");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<LiveKitParticipant>> GetParticipantsAsync(string roomName)
    {
        try
        {
            var response = await _roomService.ListParticipants(
                new ListParticipantsRequest { Room = roomName }
            );

            return response.Participants.Select(p => new LiveKitParticipant
            {
                Identity = p.Identity,
                Sid = p.Sid,
                Name = p.Name,
                State = p.State.ToString(),
                JoinedAt = p.JoinedAt.ToDateTime(),
                Tracks = p.Tracks.Select(t => new TrackInfo
                {
                    Sid = t.Sid,
                    Type = t.Type.ToString(),
                    Name = t.Name,
                    Muted = t.Muted,
                    Source = t.Source.ToString()
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get participants for room {RoomName}", roomName);
            return Enumerable.Empty<LiveKitParticipant>();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveParticipantAsync(string roomName, string participantIdentity)
    {
        try
        {
            await _roomService.RemoveParticipant(
                new RemoveParticipantRequest 
                { 
                    Room = roomName, 
                    Identity = participantIdentity 
                }
            );
            
            _logger.LogInformation("Removed participant {ParticipantIdentity} from room {RoomName}", 
                participantIdentity, roomName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove participant {ParticipantIdentity}", participantIdentity);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> MuteTrackAsync(string roomName, string participantIdentity, string trackSid, bool muted)
    {
        try
        {
            await _roomService.MutePublishedTrack(
                new MuteRoomTrackRequest
                {
                    Room = roomName,
                    Identity = participantIdentity,
                    TrackSid = trackSid,
                    Muted = muted
                }
            );
            
            _logger.LogInformation("{Action} track {TrackSid} for participant {ParticipantIdentity}", 
                muted ? "Muted" : "Unmuted", trackSid, participantIdentity);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mute track {TrackSid}", trackSid);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<RecordingInfo> StartRecordingAsync(string roomName, RecordingOptions? options = null)
    {
        try
        {
            options ??= new RecordingOptions();
            
            var request = new StartRoomCompositeRecordingRequest
            {
                RoomName = roomName,
                Output = new EncodedFileOutput
                {
                    FilePrefix = options.FilePrefix ?? $"recording-{roomName}-",
                    FileType = options.Format == "webm" ? EncodedFileType.Webm : EncodedFileType.Mp4,
                    Filepath = $"/recordings/{roomName}/{DateTime.UtcNow:yyyyMMdd-HHmmss}.{options.Format}"
                },
                Options = new RecordingOptions
                {
                    Width = options.Width,
                    Height = options.Height,
                    VideoBitrate = options.VideoBitrate,
                    AudioBitrate = options.AudioBitrate
                }
            };

            var response = await _roomService.StartRoomCompositeRecording(request);
            
            _logger.LogInformation("Started recording {RecordingId} for room {RoomName}", 
                response.RecordingId, roomName);

            return new RecordingInfo
            {
                RecordingId = response.RecordingId,
                RoomName = roomName,
                Status = "recording",
                StartedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start recording for room {RoomName}", roomName);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> StopRecordingAsync(string recordingId)
    {
        try
        {
            await _roomService.StopRecording(new StopRecordingRequest { RecordingId = recordingId });
            
            _logger.LogInformation("Stopped recording {RecordingId}", recordingId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop recording {RecordingId}", recordingId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<RecordingInfo>> ListRecordingsAsync(string? roomName = null)
    {
        try
        {
            var request = new ListRecordingsRequest();
            var response = await _roomService.ListRecordings(request);

            var recordings = response.Recordings.Select(r => new RecordingInfo
            {
                RecordingId = r.Id,
                RoomName = r.RoomName,
                Status = r.Status.ToString(),
                StartedAt = r.StartedAt.ToDateTime(),
                EndedAt = r.EndedAt?.ToDateTime(),
                FileLocation = r.FileLocation
            });

            if (!string.IsNullOrEmpty(roomName))
            {
                recordings = recordings.Where(r => r.RoomName == roomName);
            }

            return recordings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list recordings");
            return Enumerable.Empty<RecordingInfo>();
        }
    }
}

/// <summary>
/// AccessToken factory for generating LiveKit JWT tokens
/// </summary>
internal class AccessTokenFactory
{
    private readonly string _apiKey;
    private readonly string _apiSecret;

    public AccessTokenFactory(string apiKey, string apiSecret)
    {
        _apiKey = apiKey;
        _apiSecret = apiSecret;
    }

    public AccessToken CreateAccessToken(string identity, string name)
    {
        return new AccessToken(_apiKey, _apiSecret)
        {
            Identity = identity,
            Name = name
        };
    }
}

/// <summary>
/// Simple AccessToken implementation for LiveKit
/// </summary>
internal class AccessToken
{
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private VideoGrant? _videoGrant;
    private TimeSpan _ttl = TimeSpan.FromHours(6);

    public string Identity { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public AccessToken(string apiKey, string apiSecret)
    {
        _apiKey = apiKey;
        _apiSecret = apiSecret;
    }

    public void SetVideoGrant(VideoGrant grant) => _videoGrant = grant;
    public void SetTtl(TimeSpan ttl) => _ttl = ttl;

    public string ToJwt()
    {
        // In production, use a proper JWT library like System.IdentityModel.Tokens.Jwt
        // This is a simplified placeholder implementation
        var payload = new
        {
            iss = _apiKey,
            sub = Identity,
            name = Name,
            video = _videoGrant,
            exp = DateTimeOffset.UtcNow.Add(_ttl).ToUnixTimeSeconds(),
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Note: In production, properly sign this JWT with HS256 using _apiSecret
        // For now, returning a placeholder - actual implementation requires JWT library
        var jsonPayload = JsonSerializer.Serialize(payload);
        var base64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonPayload));
        
        // This is NOT a valid JWT - replace with proper JWT generation in production
        return $"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.{base64Payload}.signature";
    }
}

/// <summary>
/// Video grant permissions
/// </summary>
internal class VideoGrant
{
    public string? Room { get; set; }
    public bool RoomJoin { get; set; }
    public bool RoomCreate { get; set; }
    public bool RoomAdmin { get; set; }
    public bool CanPublish { get; set; }
    public bool CanSubscribe { get; set; }
    public bool CanPublishData { get; set; }
}

// Note: The following are placeholder types for LiveKit Protocol
// In production, use the official LiveKit.Server.SDK package types

internal class RoomServiceClient
{
    private readonly string _host;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly HttpClient _httpClient;

    public RoomServiceClient(string host, string apiKey, string apiSecret)
    {
        _host = host;
        _apiKey = apiKey;
        _apiSecret = apiSecret;
        _httpClient = new HttpClient { BaseAddress = new Uri(host) };
    }

    public async Task<Room> CreateRoom(CreateRoomRequest request)
    {
        // Placeholder - implement actual gRPC/HTTP call to LiveKit
        return new Room
        {
            Name = request.Name,
            Sid = Guid.NewGuid().ToString(),
            NumParticipants = 0,
            NumPublishers = 0,
            CreationTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            Metadata = request.Metadata
        };
    }

    public async Task<Room?> GetRoom(GetRoomRequest request)
    {
        // Placeholder
        return null;
    }

    public async Task<ListRoomsResponse> ListRooms(ListRoomsRequest request)
    {
        // Placeholder
        return new ListRoomsResponse();
    }

    public async Task DeleteRoom(DeleteRoomRequest request)
    {
        // Placeholder
    }

    public async Task<ListParticipantsResponse> ListParticipants(ListParticipantsRequest request)
    {
        // Placeholder
        return new ListParticipantsResponse();
    }

    public async Task RemoveParticipant(RemoveParticipantRequest request)
    {
        // Placeholder
    }

    public async Task MutePublishedTrack(MuteRoomTrackRequest request)
    {
        // Placeholder
    }

    public async Task<StartRecordingResponse> StartRoomCompositeRecording(StartRoomCompositeRecordingRequest request)
    {
        // Placeholder
        return new StartRecordingResponse { RecordingId = Guid.NewGuid().ToString() };
    }

    public async Task StopRecording(StopRecordingRequest request)
    {
        // Placeholder
    }

    public async Task<ListRecordingsResponse> ListRecordings(ListRecordingsRequest request)
    {
        // Placeholder
        return new ListRecordingsResponse();
    }
}

// Placeholder types for LiveKit Protocol messages
internal class CreateRoomRequest
{
    public string Name { get; set; } = string.Empty;
    public int EmptyTimeout { get; set; }
    public int MaxParticipants { get; set; }
    public string? Metadata { get; set; }
}

internal class GetRoomRequest { public string Room { get; set; } = string.Empty; }
internal class ListRoomsRequest { }
internal class DeleteRoomRequest { public string Room { get; set; } = string.Empty; }
internal class ListParticipantsRequest { public string Room { get; set; } = string.Empty; }
internal class RemoveParticipantRequest { public string Room { get; set; } = string.Empty; public string Identity { get; set; } = string.Empty; }
internal class MuteRoomTrackRequest { public string Room { get; set; } = string.Empty; public string Identity { get; set; } = string.Empty; public string TrackSid { get; set; } = string.Empty; public bool Muted { get; set; } }
internal class StartRoomCompositeRecordingRequest { public string RoomName { get; set; } = string.Empty; public EncodedFileOutput? Output { get; set; } public RecordingOptions? Options { get; set; } }
internal class StopRecordingRequest { public string RecordingId { get; set; } = string.Empty; }
internal class ListRecordingsRequest { }

internal class Room
{
    public string Name { get; set; } = string.Empty;
    public string Sid { get; set; } = string.Empty;
    public int NumParticipants { get; set; }
    public int NumPublishers { get; set; }
    public Google.Protobuf.WellKnownTypes.Timestamp CreationTime { get; set; } = new();
    public string? Metadata { get; set; }
}

internal class ListRoomsResponse { public List<Room> Rooms { get; set; } = new(); }
internal class ListParticipantsResponse { public List<Participant> Participants { get; set; } = new(); }
internal class StartRecordingResponse { public string RecordingId { get; set; } = string.Empty; }
internal class ListRecordingsResponse { public List<Recording> Recordings { get; set; } = new(); }

internal class Participant
{
    public string Identity { get; set; } = string.Empty;
    public string Sid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int State { get; set; }
    public Google.Protobuf.WellKnownTypes.Timestamp JoinedAt { get; set; } = new();
    public List<Track> Tracks { get; set; } = new();
}

internal class Track
{
    public string Sid { get; set; } = string.Empty;
    public int Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Muted { get; set; }
    public int Source { get; set; }
}

internal class Recording
{
    public string Id { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public int Status { get; set; }
    public Google.Protobuf.WellKnownTypes.Timestamp StartedAt { get; set; } = new();
    public Google.Protobuf.WellKnownTypes.Timestamp? EndedAt { get; set; }
    public string? FileLocation { get; set; }
}

internal class EncodedFileOutput
{
    public string FilePrefix { get; set; } = string.Empty;
    public EncodedFileType FileType { get; set; }
    public string Filepath { get; set; } = string.Empty;
}

internal enum EncodedFileType { Mp4, Webm }
