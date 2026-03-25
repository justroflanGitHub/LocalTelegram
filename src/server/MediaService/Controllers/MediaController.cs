using MediaService.Models;
using MediaService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MediaService.Controllers;

[ApiController]
[Route("api/media")]
[Authorize]
public class MediaController : ControllerBase
{
    private readonly IVideoProcessingService _videoService;
    private readonly ILogger<MediaController> _logger;

    public MediaController(
        IVideoProcessingService videoService,
        ILogger<MediaController> logger)
    {
        _videoService = videoService;
        _logger = logger;
    }

    /// <summary>
    /// Upload and process video file
    /// </summary>
    [HttpPost("video/upload")]
    [RequestSizeLimit(2L * 1024 * 1024 * 1024)] // 2GB limit
    public async Task<ActionResult<VideoFileDto>> UploadVideo([FromForm] VideoUploadForm form)
    {
        try
        {
            var userId = GetUserId();
            
            if (form.File == null || form.File.Length == 0)
                return BadRequest(new { error = "No file provided" });

            // Validate file type
            var allowedTypes = new[] { "video/mp4", "video/webm", "video/quicktime", "video/x-msvideo", "video/x-matroska" };
            if (!allowedTypes.Contains(form.File.ContentType))
                return BadRequest(new { error = "Invalid video format. Supported: MP4, WebM, MOV, AVI, MKV" });

            using var stream = form.File.OpenReadStream();
            
            var options = new VideoTranscodeOptions
            {
                UserId = userId,
                GenerateThumbnail = form.GenerateThumbnail,
                GenerateVariants = form.GenerateVariants,
                MaxQuality = form.MaxQuality
            };

            var videoFile = await _videoService.TranscodeVideoAsync(0, stream, options);

            // Generate variants if requested
            if (form.GenerateVariants)
            {
                var qualities = GetQualitiesForMax(form.MaxQuality);
                await _videoService.CreateVariantsAsync(videoFile.Id, qualities);
            }

            return Ok(MapToDto(videoFile));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload video");
            return StatusCode(500, new { error = "Failed to process video" });
        }
    }

    /// <summary>
    /// Get video file metadata
    /// </summary>
    [HttpGet("video/{videoId}")]
    public async Task<ActionResult<VideoFileDto>> GetVideo(long videoId)
    {
        var videoFile = await _videoService.GetVideoFileAsync(videoId);
        if (videoFile == null)
            return NotFound(new { error = "Video not found" });

        return Ok(MapToDto(videoFile));
    }

    /// <summary>
    /// Get streaming information for video
    /// </summary>
    [HttpGet("video/{videoId}/streaming")]
    [AllowAnonymous] // Allow for video player access
    public async Task<ActionResult<VideoStreamingInfo>> GetStreamingInfo(long videoId)
    {
        var info = await _videoService.GetStreamingInfoAsync(videoId);
        if (info == null)
            return NotFound(new { error = "Video not found" });

        return Ok(info);
    }

    /// <summary>
    /// Get video stream URL for specific quality
    /// </summary>
    [HttpGet("video/{videoId}/stream/{quality}")]
    [AllowAnonymous]
    public async Task<ActionResult> StreamVideo(long videoId, VideoQuality quality)
    {
        try
        {
            var url = await _videoService.GetStreamingUrlAsync(videoId, quality);
            return Redirect(url);
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { error = "Video variant not found" });
        }
    }

    /// <summary>
    /// Generate video thumbnail
    /// </summary>
    [HttpPost("video/thumbnail")]
    public async Task<ActionResult> GenerateThumbnail([FromForm] ThumbnailForm form)
    {
        if (form.File == null || form.File.Length == 0)
            return BadRequest(new { error = "No file provided" });

        using var stream = form.File.OpenReadStream();
        var thumbnail = await _videoService.GenerateThumbnailAsync(stream, form.Width, form.Height, form.TimestampSeconds);

        return File(thumbnail, "image/jpeg", "thumbnail.jpg");
    }

    /// <summary>
    /// Generate animated GIF preview
    /// </summary>
    [HttpPost("video/animated-preview")]
    public async Task<ActionResult> GenerateAnimatedPreview([FromForm] AnimatedPreviewForm form)
    {
        if (form.File == null || form.File.Length == 0)
            return BadRequest(new { error = "No file provided" });

        using var stream = form.File.OpenReadStream();
        var gif = await _videoService.GenerateAnimatedPreviewAsync(stream, form.Width, form.DurationSeconds, form.Fps);

        return File(gif, "image/gif", "preview.gif");
    }

    /// <summary>
    /// Upload round video message (video circle)
    /// </summary>
    [HttpPost("round-video")]
    [RequestSizeLimit(100L * 1024 * 1024)] // 100MB limit for round videos
    public async Task<ActionResult<VideoMessageDto>> UploadRoundVideo([FromForm] RoundVideoForm form)
    {
        try
        {
            var userId = GetUserId();
            
            if (form.File == null || form.File.Length == 0)
                return BadRequest(new { error = "No file provided" });

            using var stream = form.File.OpenReadStream();
            var videoMessage = await _videoService.ProcessRoundVideoAsync(form.FileId, stream, userId);

            return Ok(new VideoMessageDto
            {
                Id = videoMessage.Id,
                FileId = videoMessage.FileId,
                DurationSeconds = videoMessage.DurationSeconds,
                Width = videoMessage.Width,
                Height = videoMessage.Height,
                AnimatedThumbnailId = videoMessage.AnimatedThumbnailId,
                CreatedAt = videoMessage.CreatedAt
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process round video");
            return StatusCode(500, new { error = "Failed to process round video" });
        }
    }

    /// <summary>
    /// Extract video metadata
    /// </summary>
    [HttpPost("video/metadata")]
    public async Task<ActionResult<VideoMetadata>> GetMetadata([FromForm] MetadataForm form)
    {
        if (form.File == null || form.File.Length == 0)
            return BadRequest(new { error = "No file provided" });

        using var stream = form.File.OpenReadStream();
        var metadata = await _videoService.ExtractMetadataAsync(stream, form.File.FileName);

        return Ok(metadata);
    }

    /// <summary>
    /// Create additional quality variants for existing video
    /// </summary>
    [HttpPost("video/{videoId}/variants")]
    public async Task<ActionResult<List<VideoVariantInfo>>> CreateVariants(long videoId, [FromBody] CreateVariantsRequest request)
    {
        try
        {
            var variants = await _videoService.CreateVariantsAsync(videoId, request.Qualities);
            
            var result = variants.Select(v => new VideoVariantInfo
            {
                Quality = v.Quality,
                Width = v.Width,
                Height = v.Height,
                BitrateKbps = v.BitrateKbps,
                FileSizeBytes = v.FileSizeBytes,
                StreamingUrl = $"/api/media/video/{videoId}/stream/{v.Quality}"
            }).ToList();

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    #region Helper Methods

    private long GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(userIdClaim, out var userId) ? userId : 0;
    }

    private static VideoFileDto MapToDto(VideoFile video)
    {
        return new VideoFileDto
        {
            Id = video.Id,
            OriginalFileId = video.OriginalFileId,
            DurationSeconds = video.DurationSeconds,
            Width = video.Width,
            Height = video.Height,
            Codec = video.Codec,
            BitrateKbps = video.BitrateKbps,
            FrameRate = video.FrameRate,
            FileSizeBytes = video.FileSizeBytes,
            MimeType = video.MimeType,
            HasAudio = video.HasAudio,
            AudioCodec = video.AudioCodec,
            ThumbnailFileId = video.ThumbnailFileId,
            Status = video.Status,
            ErrorMessage = video.ErrorMessage,
            CreatedAt = video.CreatedAt
        };
    }

    private static List<VideoQuality> GetQualitiesForMax(VideoQuality maxQuality)
    {
        var allQualities = new List<VideoQuality>
        {
            VideoQuality.Low,
            VideoQuality.Medium,
            VideoQuality.Standard,
            VideoQuality.HD,
            VideoQuality.FullHD,
            VideoQuality.QHD,
            VideoQuality.UltraHD
        };

        return allQualities.Where(q => q <= maxQuality).ToList();
    }

    #endregion
}

#region Form Models

public class VideoUploadForm
{
    public IFormFile? File { get; set; }
    public bool GenerateThumbnail { get; set; } = true;
    public bool GenerateVariants { get; set; } = true;
    public VideoQuality MaxQuality { get; set; } = VideoQuality.FullHD;
}

public class ThumbnailForm
{
    public IFormFile? File { get; set; }
    public int Width { get; set; } = 320;
    public int Height { get; set; } = 240;
    public int TimestampSeconds { get; set; } = 0;
}

public class AnimatedPreviewForm
{
    public IFormFile? File { get; set; }
    public int Width { get; set; } = 320;
    public int DurationSeconds { get; set; } = 5;
    public int Fps { get; set; } = 10;
}

public class RoundVideoForm
{
    public IFormFile? File { get; set; }
    public long FileId { get; set; }
}

public class MetadataForm
{
    public IFormFile? File { get; set; }
}

public class CreateVariantsRequest
{
    public List<VideoQuality> Qualities { get; set; } = new();
}

#endregion
