using FileService.Models;
using FileService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FileService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ImagesController : ControllerBase
{
    private readonly IImageProcessingService _imageService;
    private readonly IFileService _fileService;
    private readonly IStorageService _storageService;
    private readonly ILogger<ImagesController> _logger;

    public ImagesController(
        IImageProcessingService imageService,
        IFileService fileService,
        IStorageService storageService,
        ILogger<ImagesController> logger)
    {
        _imageService = imageService;
        _fileService = fileService;
        _storageService = storageService;
        _logger = logger;
    }

    /// <summary>
    /// Upload and compress an image
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(100_000_000)] // 100MB
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadImage(
        [FromForm] IFormFile file,
        [FromForm] int? quality,
        [FromForm] int? maxWidth,
        [FromForm] int? maxHeight,
        [FromForm] bool removeMetadata = true)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file provided" });
        }

        if (!_imageService.IsSupportedImage(file.ContentType))
        {
            return BadRequest(new { error = "Unsupported image format" });
        }

        try
        {
            var options = new ImageCompressionOptions
            {
                Quality = quality ?? 85,
                MaxWidth = maxWidth ?? 2048,
                MaxHeight = maxHeight ?? 2048,
                RemoveMetadata = removeMetadata
            };

            using var inputStream = file.OpenReadStream();
            using var compressedStream = await _imageService.CompressImageAsync(
                inputStream, file.ContentType, options);

            // Create a new FormFile from the compressed stream
            var compressedFile = new FormFile(
                compressedStream, 
                0, 
                compressedStream.Length, 
                "file", 
                file.FileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = file.ContentType
            };

            var metadata = await _fileService.UploadFileAsync(userId.Value, compressedFile);
            if (metadata == null)
            {
                return BadRequest(new { error = "Failed to upload image" });
            }

            var thumbnailUrl = metadata.ThumbnailPath != null
                ? await _storageService.GetPresignedUrlAsync(metadata.ThumbnailPath)
                : null;

            return Ok(new UploadResponse
            {
                FileId = metadata.Id,
                OriginalName = metadata.OriginalName,
                MimeType = metadata.MimeType,
                SizeBytes = metadata.SizeBytes,
                ThumbnailUrl = thumbnailUrl,
                CreatedAt = metadata.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading image");
            return BadRequest(new { error = "Failed to process image" });
        }
    }

    /// <summary>
    /// Remove EXIF data from an existing image
    /// </summary>
    [HttpPost("{fileId}/remove-exif")]
    [ProducesResponseType(typeof(FileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RemoveExifData(long fileId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var file = await _fileService.GetFileAsync(fileId);
        if (file == null)
        {
            return NotFound();
        }

        if (!_imageService.IsSupportedImage(file.MimeType))
        {
            return BadRequest(new { error = "File is not a supported image" });
        }

        try
        {
            var downloadStream = await _fileService.DownloadFileAsync(fileId);
            if (downloadStream == null)
            {
                return NotFound();
            }

            using var cleanedStream = await _imageService.RemoveExifDataAsync(downloadStream, file.MimeType);

            // Re-upload the cleaned image
            var objectName = $"images/{Guid.NewGuid()}{Path.GetExtension(file.OriginalName)}";
            await _storageService.UploadFileAsync(objectName, cleanedStream, cleanedStream.Length, file.MimeType);

            // Update file metadata
            file.StoragePath = objectName;
            file.SizeBytes = cleanedStream.Length;

            var thumbnailUrl = file.ThumbnailPath != null
                ? await _storageService.GetPresignedUrlAsync(file.ThumbnailPath)
                : null;

            return Ok(FileDto.FromMetadata(file, thumbnailUrl));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing EXIF data");
            return BadRequest(new { error = "Failed to remove EXIF data" });
        }
    }

    /// <summary>
    /// Get image metadata
    /// </summary>
    [HttpGet("{fileId}/metadata")]
    [ProducesResponseType(typeof(ImageMetadataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetImageMetadata(long fileId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var file = await _fileService.GetFileAsync(fileId);
        if (file == null)
        {
            return NotFound();
        }

        if (!_imageService.IsSupportedImage(file.MimeType))
        {
            return BadRequest(new { error = "File is not a supported image" });
        }

        try
        {
            var downloadStream = await _fileService.DownloadFileAsync(fileId);
            if (downloadStream == null)
            {
                return NotFound();
            }

            var metadata = await _imageService.ExtractMetadataAsync(downloadStream);

            return Ok(new ImageMetadataResponse
            {
                FileId = fileId,
                Width = metadata.Width,
                Height = metadata.Height,
                CameraMake = metadata.CameraMake,
                CameraModel = metadata.CameraModel,
                TakenAt = metadata.TakenAt,
                Latitude = metadata.Latitude,
                Longitude = metadata.Longitude,
                Software = metadata.Software,
                Orientation = metadata.Orientation,
                IsoSpeed = metadata.IsoSpeed,
                FNumber = metadata.FNumber,
                ExposureTime = metadata.ExposureTime,
                FocalLength = metadata.FocalLength
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting image metadata");
            return BadRequest(new { error = "Failed to extract metadata" });
        }
    }

    /// <summary>
    /// Resize an image
    /// </summary>
    [HttpPost("{fileId}/resize")]
    [ProducesResponseType(typeof(FileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ResizeImage(
        long fileId,
        [FromQuery] int maxWidth = 1024,
        [FromQuery] int maxHeight = 1024)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var file = await _fileService.GetFileAsync(fileId);
        if (file == null)
        {
            return NotFound();
        }

        if (!_imageService.IsSupportedImage(file.MimeType))
        {
            return BadRequest(new { error = "File is not a supported image" });
        }

        try
        {
            var downloadStream = await _fileService.DownloadFileAsync(fileId);
            if (downloadStream == null)
            {
                return NotFound();
            }

            using var resizedStream = await _imageService.ResizeImageAsync(
                downloadStream, file.MimeType, maxWidth, maxHeight);

            // Create new file for resized image
            var objectName = $"images/{Guid.NewGuid()}{Path.GetExtension(file.OriginalName)}";
            await _storageService.UploadFileAsync(objectName, resizedStream, resizedStream.Length, file.MimeType);

            var newFile = new FileMetadata
            {
                UploaderId = userId.Value,
                OriginalName = $"resized_{file.OriginalName}",
                MimeType = file.MimeType,
                SizeBytes = resizedStream.Length,
                StoragePath = objectName,
                UploadCompletedAt = DateTime.UtcNow
            };

            // Generate thumbnail
            resizedStream.Position = 0;
            var thumbnailStream = await _imageService.CreateThumbnailAsync(resizedStream, file.MimeType, 200);
            var thumbnailPath = $"thumbnails/{Guid.NewGuid()}.jpg";
            await _storageService.UploadFileAsync(thumbnailPath, thumbnailStream, thumbnailStream.Length, "image/jpeg");
            newFile.ThumbnailPath = thumbnailPath;

            var thumbnailUrl = await _storageService.GetPresignedUrlAsync(thumbnailPath);

            return Ok(FileDto.FromMetadata(newFile, thumbnailUrl));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resizing image");
            return BadRequest(new { error = "Failed to resize image" });
        }
    }

    /// <summary>
    /// Create a thumbnail from an image
    /// </summary>
    [HttpPost("{fileId}/thumbnail")]
    [ProducesResponseType(typeof(ThumbnailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateThumbnail(
        long fileId,
        [FromQuery] int size = 200)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var file = await _fileService.GetFileAsync(fileId);
        if (file == null)
        {
            return NotFound();
        }

        if (!_imageService.IsSupportedImage(file.MimeType))
        {
            return BadRequest(new { error = "File is not a supported image" });
        }

        try
        {
            var downloadStream = await _fileService.DownloadFileAsync(fileId);
            if (downloadStream == null)
            {
                return NotFound();
            }

            using var thumbnailStream = await _imageService.CreateThumbnailAsync(
                downloadStream, file.MimeType, size);

            var thumbnailPath = $"thumbnails/{Guid.NewGuid()}.jpg";
            await _storageService.UploadFileAsync(
                thumbnailPath, thumbnailStream, thumbnailStream.Length, "image/jpeg");

            var thumbnailUrl = await _storageService.GetPresignedUrlAsync(thumbnailPath);

            return Ok(new ThumbnailResponse
            {
                FileId = fileId,
                ThumbnailPath = thumbnailPath,
                ThumbnailUrl = thumbnailUrl,
                Size = size
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating thumbnail");
            return BadRequest(new { error = "Failed to create thumbnail" });
        }
    }

    private long? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (long.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }
        return null;
    }
}

public class ImageMetadataResponse
{
    public long FileId { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }
    public DateTime? TakenAt { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Software { get; set; }
    public int? Orientation { get; set; }
    public int? IsoSpeed { get; set; }
    public double? FNumber { get; set; }
    public double? ExposureTime { get; set; }
    public double? FocalLength { get; set; }
}

public class ThumbnailResponse
{
    public long FileId { get; set; }
    public string ThumbnailPath { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public int Size { get; set; }
}
