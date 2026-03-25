using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MediaService.Models;

/// <summary>
/// Represents a processed video file
/// </summary>
public class VideoFile
{
    [Key]
    public long Id { get; set; }
    
    /// <summary>
    /// Original file ID from FileService
    /// </summary>
    public long OriginalFileId { get; set; }
    
    /// <summary>
    /// Video duration in seconds
    /// </summary>
    public int DurationSeconds { get; set; }
    
    /// <summary>
    /// Video width in pixels
    /// </summary>
    public int Width { get; set; }
    
    /// <summary>
    /// Video height in pixels
    /// </summary>
    public int Height { get; set; }
    
    /// <summary>
    /// Video codec (e.g., h264, h265, vp9)
    /// </summary>
    [MaxLength(20)]
    public string Codec { get; set; } = "h264";
    
    /// <summary>
    /// Bitrate in kbps
    /// </summary>
    public int BitrateKbps { get; set; }
    
    /// <summary>
    /// Frame rate
    /// </summary>
    public double FrameRate { get; set; }
    
    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSizeBytes { get; set; }
    
    /// <summary>
    /// MIME type
    /// </summary>
    [MaxLength(50)]
    public string MimeType { get; set; } = "video/mp4";
    
    /// <summary>
    /// Has audio track
    /// </summary>
    public bool HasAudio { get; set; }
    
    /// <summary>
    /// Audio codec if present
    /// </summary>
    [MaxLength(20)]
    public string? AudioCodec { get; set; }
    
    /// <summary>
    /// Preview/thumbnail file ID
    /// </summary>
    public long? ThumbnailFileId { get; set; }
    
    /// <summary>
    /// Processing status
    /// </summary>
    public VideoProcessingStatus Status { get; set; } = VideoProcessingStatus.Pending;
    
    /// <summary>
    /// Processing error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// User ID who uploaded
    /// </summary>
    public long UserId { get; set; }
    
    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a transcoded video variant (different quality/resolution)
/// </summary>
public class VideoVariant
{
    [Key]
    public long Id { get; set; }
    
    /// <summary>
    /// Parent video file ID
    /// </summary>
    public long VideoFileId { get; set; }
    
    /// <summary>
    /// Quality level
    /// </summary>
    public VideoQuality Quality { get; set; }
    
    /// <summary>
    /// Width in pixels
    /// </summary>
    public int Width { get; set; }
    
    /// <summary>
    /// Height in pixels
    /// </summary>
    public int Height { get; set; }
    
    /// <summary>
    /// Bitrate in kbps
    /// </summary>
    public int BitrateKbps { get; set; }
    
    /// <summary>
    /// File path in storage
    /// </summary>
    [MaxLength(500)]
    public string StoragePath { get; set; } = string.Empty;
    
    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSizeBytes { get; set; }
    
    /// <summary>
    /// Navigation to parent video
    /// </summary>
    [JsonIgnore]
    public VideoFile VideoFile { get; set; } = null!;
}

/// <summary>
/// Represents a video message (round video)
/// </summary>
public class VideoMessage
{
    [Key]
    public long Id { get; set; }
    
    /// <summary>
    /// File ID from FileService
    /// </summary>
    public long FileId { get; set; }
    
    /// <summary>
    /// Duration in seconds (max 60 for round videos)
    /// </summary>
    public int DurationSeconds { get; set; }
    
    /// <summary>
    /// Dimensions (typically 384x384 for round video)
    /// </summary>
    public int Width { get; set; } = 384;
    
    public int Height { get; set; } = 384;
    
    /// <summary>
    /// Animated thumbnail (GIF)
    /// </summary>
    public long? AnimatedThumbnailId { get; set; }
    
    /// <summary>
    /// User ID who sent
    /// </summary>
    public long UserId { get; set; }
    
    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Processing status for video files
/// </summary>
public enum VideoProcessingStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}

/// <summary>
/// Video quality levels for adaptive streaming
/// </summary>
public enum VideoQuality
{
    /// <summary>
    /// 240p - Low quality for slow connections
    /// </summary>
    Low = 0,
    
    /// <summary>
    /// 360p - Medium quality
    /// </summary>
    Medium = 1,
    
    /// <summary>
    /// 480p - Standard quality
    /// </summary>
    Standard = 2,
    
    /// <summary>
    /// 720p - HD quality
    /// </summary>
    HD = 3,
    
    /// <summary>
    /// 1080p - Full HD quality
    /// </summary>
    FullHD = 4,
    
    /// <summary>
    /// 1440p - 2K quality
    /// </summary>
    QHD = 5,
    
    /// <summary>
    /// 2160p - 4K quality
    /// </summary>
    UltraHD = 6
}

/// <summary>
/// Video metadata extracted from file
/// </summary>
public class VideoMetadata
{
    public int DurationSeconds { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Codec { get; set; } = string.Empty;
    public int BitrateKbps { get; set; }
    public double FrameRate { get; set; }
    public long FileSizeBytes { get; set; }
    public bool HasAudio { get; set; }
    public string? AudioCodec { get; set; }
    public int? AudioSampleRate { get; set; }
    public int? AudioChannels { get; set; }
    public DateTime? CreationTime { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Rotation { get; set; }
}

#region DTOs

public class VideoUploadRequest
{
    public long FileId { get; set; }
    public bool GenerateVariants { get; set; } = true;
    public VideoQuality MaxQuality { get; set; } = VideoQuality.FullHD;
    public bool GenerateThumbnail { get; set; } = true;
}

public class VideoTranscodeRequest
{
    public long VideoFileId { get; set; }
    public List<VideoQuality> Qualities { get; set; } = new();
    public string OutputFormat { get; set; } = "mp4";
    public int? MaxBitrateKbps { get; set; }
}

public class VideoStreamingInfo
{
    public long VideoId { get; set; }
    public int DurationSeconds { get; set; }
    public List<VideoVariantInfo> Variants { get; set; } = new();
    public string? ThumbnailUrl { get; set; }
}

public class VideoVariantInfo
{
    public VideoQuality Quality { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int BitrateKbps { get; set; }
    public string StreamingUrl { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
}

public class VideoFileDto
{
    public long Id { get; set; }
    public long OriginalFileId { get; set; }
    public int DurationSeconds { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Codec { get; set; } = string.Empty;
    public int BitrateKbps { get; set; }
    public double FrameRate { get; set; }
    public long FileSizeBytes { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public bool HasAudio { get; set; }
    public string? AudioCodec { get; set; }
    public long? ThumbnailFileId { get; set; }
    public VideoProcessingStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class VideoMessageDto
{
    public long Id { get; set; }
    public long FileId { get; set; }
    public int DurationSeconds { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long? AnimatedThumbnailId { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? VideoUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}

#endregion
