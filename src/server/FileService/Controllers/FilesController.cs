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
