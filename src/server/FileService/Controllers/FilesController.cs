using FileService.Models;
using FileService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FileService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;
    private readonly IStorageService _storageService;
    private readonly ILogger<FilesController> _logger;

    public FilesController(
        IFileService fileService,
        IStorageService storageService,
        ILogger<FilesController> logger)
    {
        _fileService = fileService;
        _storageService = storageService;
        _logger = logger;
    }

    /// <summary>
    /// Upload a file
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(2147483648)] // 2GB
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadFile([FromForm] IFormFile file)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file provided" });
        }

        var metadata = await _fileService.UploadFileAsync(userId.Value, file);
        if (metadata == null)
        {
            return BadRequest(new { error = "Failed to upload file" });
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

    /// <summary>
    /// Get file metadata
    /// </summary>
    [HttpGet("{fileId}")]
    [ProducesResponseType(typeof(FileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetFile(long fileId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var metadata = await _fileService.GetFileAsync(fileId);
        if (metadata == null)
        {
            return NotFound();
        }

        var thumbnailUrl = metadata.ThumbnailPath != null
            ? await _storageService.GetPresignedUrlAsync(metadata.ThumbnailPath)
            : null;

        return Ok(FileDto.FromMetadata(metadata, thumbnailUrl));
    }

    /// <summary>
    /// Download a file
    /// </summary>
    [HttpGet("{fileId}/download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DownloadFile(long fileId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var metadata = await _fileService.GetFileAsync(fileId);
        if (metadata == null)
        {
            return NotFound();
        }

        var stream = await _fileService.DownloadFileAsync(fileId);
        if (stream == null)
        {
            return NotFound();
        }

        return File(stream, metadata.MimeType ?? "application/octet-stream", metadata.OriginalName);
    }

    /// <summary>
    /// Get presigned URL for direct download
    /// </summary>
    [HttpGet("{fileId}/url")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPresignedUrl(long fileId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var url = await _fileService.GetPresignedUrlAsync(fileId);
        if (url == null)
        {
            return NotFound();
        }

        return Ok(new { url });
    }

    /// <summary>
    /// Delete a file
    /// </summary>
    [HttpDelete("{fileId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteFile(long fileId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var success = await _fileService.DeleteFileAsync(fileId, userId.Value);
        if (!success)
        {
            return BadRequest(new { error = "Failed to delete file. You may not have permission." });
        }

        return Ok(new { message = "File deleted" });
    }

    /// <summary>
    /// Get thumbnail for a file
    /// </summary>
    [HttpGet("{fileId}/thumbnail")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetThumbnail(long fileId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var metadata = await _fileService.GetFileAsync(fileId);
        if (metadata == null || string.IsNullOrEmpty(metadata.ThumbnailPath))
        {
            return NotFound();
        }

        var stream = await _storageService.DownloadFileAsync(metadata.ThumbnailPath);
        return File(stream, "image/jpeg");
    }

    #region Chunked Upload

    /// <summary>
    /// Initialize a chunked upload session for large files
    /// </summary>
    [HttpPost("chunked/init")]
    [ProducesResponseType(typeof(ChunkedUploadInitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> InitChunkedUpload([FromBody] ChunkedUploadInitRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (request == null || string.IsNullOrEmpty(request.FileName) || request.FileSize <= 0)
        {
            return BadRequest(new { error = "Invalid request. FileName and FileSize are required." });
        }

        var session = await _fileService.InitChunkedUploadAsync(userId.Value, request);
        if (session == null)
        {
            return BadRequest(new { error = "Failed to initialize chunked upload" });
        }

        return Ok(new ChunkedUploadInitResponse
        {
            UploadId = session.UploadId,
            ChunkSize = session.ChunkSize,
            TotalChunks = session.TotalChunks
        });
    }

    /// <summary>
    /// Upload a chunk of a file
    /// </summary>
    [HttpPost("chunked/{uploadId}/chunks/{chunkNumber}")]
    [RequestSizeLimit(10485760)] // 10MB max per chunk
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadChunk(string uploadId, int chunkNumber, [FromForm] IFormFile chunk)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (chunk == null || chunk.Length == 0)
        {
            return BadRequest(new { error = "No chunk data provided" });
        }

        // Verify session belongs to user
        var session = await _fileService.GetUploadSessionAsync(uploadId);
        if (session == null)
        {
            return NotFound(new { error = "Upload session not found" });
        }

        if (session.UploaderId != userId.Value)
        {
            return Unauthorized();
        }

        using var stream = chunk.OpenReadStream();
        var success = await _fileService.UploadChunkAsync(uploadId, chunkNumber, stream, chunk.Length);
        
        if (!success)
        {
            return BadRequest(new { error = "Failed to upload chunk" });
        }

        return Ok(new { message = $"Chunk {chunkNumber} uploaded successfully" });
    }

    /// <summary>
    /// Get chunked upload status
    /// </summary>
    [HttpGet("chunked/{uploadId}/status")]
    [ProducesResponseType(typeof(ChunkUploadStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChunkedUploadStatus(string uploadId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var session = await _fileService.GetUploadSessionAsync(uploadId);
        if (session == null)
        {
            return NotFound(new { error = "Upload session not found" });
        }

        if (session.UploaderId != userId.Value)
        {
            return Unauthorized();
        }

        var missingChunks = Enumerable.Range(0, session.TotalChunks)
            .Except(session.UploadedChunks)
            .ToList();

        return Ok(new ChunkUploadStatus
        {
            UploadId = uploadId,
            TotalChunks = session.TotalChunks,
            UploadedChunks = session.UploadedChunks.Count,
            MissingChunks = missingChunks
        });
    }

    /// <summary>
    /// Complete a chunked upload
    /// </summary>
    [HttpPost("chunked/{uploadId}/complete")]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompleteChunkedUpload(string uploadId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var session = await _fileService.GetUploadSessionAsync(uploadId);
        if (session == null)
        {
            return NotFound(new { error = "Upload session not found" });
        }

        if (session.UploaderId != userId.Value)
        {
            return Unauthorized();
        }

        var metadata = await _fileService.CompleteChunkedUploadAsync(uploadId);
        if (metadata == null)
        {
            return BadRequest(new { error = "Failed to complete upload. Not all chunks uploaded." });
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

    /// <summary>
    /// Abort a chunked upload
    /// </summary>
    [HttpDelete("chunked/{uploadId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AbortChunkedUpload(string uploadId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var session = await _fileService.GetUploadSessionAsync(uploadId);
        if (session == null)
        {
            return NotFound(new { error = "Upload session not found" });
        }

        if (session.UploaderId != userId.Value)
        {
            return Unauthorized();
        }

        var success = await _fileService.AbortChunkedUploadAsync(uploadId);
        if (!success)
        {
            return NotFound();
        }

        return Ok(new { message = "Upload aborted" });
    }

    #endregion

    #region Preview Generation

    /// <summary>
    /// Generate previews for a file
    /// </summary>
    [HttpPost("{fileId}/previews")]
    [ProducesResponseType(typeof(FilePreview), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GeneratePreviews(long fileId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var metadata = await _fileService.GetFileAsync(fileId);
        if (metadata == null)
        {
            return NotFound();
        }

        var preview = new FilePreview { FileId = fileId };

        // Generate image previews
        if (metadata.MimeType?.StartsWith("image/") == true)
        {
            var previewPaths = await _fileService.GenerateImagePreviewsAsync(metadata.StoragePath, metadata.MimeType);
            if (previewPaths.Count >= 1)
                preview.SmallPreviewUrl = await _storageService.GetPresignedUrlAsync(previewPaths[0]);
            if (previewPaths.Count >= 2)
                preview.MediumPreviewUrl = await _storageService.GetPresignedUrlAsync(previewPaths[1]);
            if (previewPaths.Count >= 3)
                preview.LargePreviewUrl = await _storageService.GetPresignedUrlAsync(previewPaths[2]);
        }

        // Generate video preview
        if (metadata.MimeType?.StartsWith("video/") == true)
        {
            var videoPreviewPath = await _fileService.GenerateVideoPreviewAsync(metadata.StoragePath);
            if (videoPreviewPath != null)
                preview.VideoPreviewUrl = await _storageService.GetPresignedUrlAsync(videoPreviewPath);
        }

        // Generate document preview
        if (metadata.MimeType == "application/pdf")
        {
            var docPreviewPath = await _fileService.GenerateDocumentPreviewAsync(metadata.StoragePath, metadata.MimeType);
            if (docPreviewPath != null)
                preview.DocumentPreviewUrl = await _storageService.GetPresignedUrlAsync(docPreviewPath);
        }

        return Ok(preview);
    }

    /// <summary>
    /// Get preview for a file at specific size
    /// </summary>
    [HttpGet("{fileId}/preview/{size}")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPreview(long fileId, string size)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var metadata = await _fileService.GetFileAsync(fileId);
        if (metadata == null)
        {
            return NotFound();
        }

        // Construct preview path based on size
        var previewPath = $"previews/{metadata.StoragePath}.{size}.jpg";
        var stream = await _storageService.DownloadFileAsync(previewPath);
        
        if (stream == null)
        {
            return NotFound();
        }

        return File(stream, "image/jpeg");
    }

    #endregion

    private long? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !long.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        return userId;
    }
}
