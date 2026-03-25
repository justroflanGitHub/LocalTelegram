namespace FileService.Models;

/// <summary>
/// Represents a voice message with waveform data
/// </summary>
public class VoiceMessage
{
    public long Id { get; set; }
    public long FileId { get; set; }
    public int DurationSeconds { get; set; }
    public string Waveform { get; set; } = string.Empty; // Base64 encoded waveform samples
    public string Codec { get; set; } = "opus";
    public int SampleRate { get; set; } = 48000;
    public int Channels { get; set; } = 1;
    public int Bitrate { get; set; } = 64000;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation property
    public FileMetadata? File { get; set; }
}

/// <summary>
/// Request model for uploading a voice message
/// </summary>
public class VoiceMessageUploadRequest
{
    public IFormFile AudioFile { get; set; } = null!;
    public int DurationSeconds { get; set; }
    public string? Waveform { get; set; } // Optional pre-computed waveform
}

/// <summary>
/// DTO for voice message response
/// </summary>
public class VoiceMessageDto
{
    public long Id { get; set; }
    public long FileId { get; set; }
    public int DurationSeconds { get; set; }
    public string Waveform { get; set; } = string.Empty;
    public string Codec { get; set; } = string.Empty;
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public string? DownloadUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    
    public static VoiceMessageDto FromVoiceMessage(VoiceMessage voice, string? downloadUrl = null)
    {
        return new VoiceMessageDto
        {
            Id = voice.Id,
            FileId = voice.FileId,
            DurationSeconds = voice.DurationSeconds,
            Waveform = voice.Waveform,
            Codec = voice.Codec,
            SampleRate = voice.SampleRate,
            Channels = voice.Channels,
            DownloadUrl = downloadUrl,
            CreatedAt = voice.CreatedAt
        };
    }
}

/// <summary>
/// Waveform sample data
/// </summary>
public class WaveformSample
{
    public int Min { get; set; }
    public int Max { get; set; }
}
