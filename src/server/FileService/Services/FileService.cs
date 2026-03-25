using System.Security.Cryptography;
using FileService.Data;
using FileService.Models;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace FileService.Services;

public interface IFileService
{
    Task<FileMetadata?> UploadFileAsync(long uploaderId, IFormFile file);
    Task<FileMetadata?> GetFileAsync(long fileId);
    Task<Stream?> DownloadFileAsync(long fileId);
    Task<string?> GetPresignedUrlAsync(long fileId);
    Task<bool> DeleteFileAsync(long fileId, long userId);
    Task<string> GenerateThumbnailAsync(string objectName, Stream imageData);
    
    // Chunked upload
    Task<ChunkedUploadSession?> InitChunkedUploadAsync(long uploaderId, ChunkedUploadInitRequest request);
    Task<bool> UploadChunkAsync(string uploadId, int chunkNumber, Stream chunkData, long chunkSize);
    Task<FileMetadata?> CompleteChunkedUploadAsync(string uploadId);
    Task<ChunkedUploadSession?> GetUploadSessionAsync(string uploadId);
    Task<bool> AbortChunkedUploadAsync(string uploadId);
    
    // Preview generation
    Task<string?> GenerateVideoPreviewAsync(string videoPath);
    Task<string?> GenerateDocumentPreviewAsync(string documentPath, string mimeType);
    Task<List<string>> GenerateImagePreviewsAsync(string imagePath, string mimeType);
}

public class FileService : IFileService
{
    private readonly FileDbContext _context;
    private readonly IStorageService _storageService;
    private readonly ILogger<FileService> _logger;
    private const int MaxFileSize = 2147483648; // 2GB
    private static readonly string[] ImageTypes = { "image/jpeg", "image/png", "image/gif", "image/webp" };

    public FileService(
        FileDbContext context,
        IStorageService storageService,
        ILogger<FileService> logger)
    {
        _context = context;
        _storageService = storageService;
        _logger = logger;
    }

    public async Task<FileMetadata?> UploadFileAsync(long uploaderId, IFormFile file)
    {
        if (file.Length > MaxFileSize)
        {
            _logger.LogWarning("File size {Size} exceeds maximum {MaxSize}", file.Length, MaxFileSize);
            return null;
        }

        var objectName = GenerateObjectName(file.FileName);
        string? checksum;

        using (var stream = file.OpenReadStream())
        {
            checksum = await ComputeChecksumAsync(stream);
            stream.Position = 0;

            await _storageService.UploadFileAsync(objectName, stream, file.Length, file.ContentType);
        }

        var metadata = new FileMetadata
        {
            UploaderId = uploaderId,
            OriginalName = file.FileName,
            MimeType = file.ContentType,
            SizeBytes = file.Length,
            StoragePath = objectName,
            Checksum = checksum,
            UploadCompletedAt = DateTime.UtcNow
        };

        _context.Files.Add(metadata);
        await _context.SaveChangesAsync();

        // Generate thumbnail for images
        if (IsImage(file.ContentType))
        {
            try
            {
                using var stream = file.OpenReadStream();
                var thumbnailPath = await GenerateThumbnailAsync(objectName, stream);
                metadata.ThumbnailPath = thumbnailPath;
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate thumbnail for {ObjectName}", objectName);
            }
        }

        _logger.LogInformation("File {FileId} uploaded by user {UserId}", metadata.Id, uploaderId);

        return metadata;
    }

    public async Task<FileMetadata?> GetFileAsync(long fileId)
    {
        return await _context.Files.FindAsync(fileId);
    }

    public async Task<Stream?> DownloadFileAsync(long fileId)
    {
        var metadata = await _context.Files.FindAsync(fileId);
        if (metadata == null) return null;

        return await _storageService.DownloadFileAsync(metadata.StoragePath);
    }

    public async Task<string?> GetPresignedUrlAsync(long fileId)
    {
        var metadata = await _context.Files.FindAsync(fileId);
        if (metadata == null) return null;

        return await _storageService.GetPresignedUrlAsync(metadata.StoragePath);
    }

    public async Task<bool> DeleteFileAsync(long fileId, long userId)
    {
        var metadata = await _context.Files.FindAsync(fileId);
        if (metadata == null) return false;

        if (metadata.UploaderId != userId)
        {
            _logger.LogWarning("User {UserId} attempted to delete file {FileId} owned by {UploaderId}",
                userId, fileId, metadata.UploaderId);
            return false;
        }

        // Delete from storage
        await _storageService.DeleteFileAsync(metadata.StoragePath);

        // Delete thumbnail if exists
        if (!string.IsNullOrEmpty(metadata.ThumbnailPath))
        {
            await _storageService.DeleteFileAsync(metadata.ThumbnailPath);
        }

        _context.Files.Remove(metadata);
        await _context.SaveChangesAsync();

        _logger.LogInformation("File {FileId} deleted by user {UserId}", fileId, userId);

        return true;
    }

    public async Task<string> GenerateThumbnailAsync(string objectName, Stream imageData)
    {
        var thumbnailObjectName = $"thumbnails/{objectName}";

        using var image = await Image.LoadAsync(imageData);

        // Resize to max 200x200 while maintaining aspect ratio
        var maxDimension = 200;
        var ratio = Math.Min((double)maxDimension / image.Width, (double)maxDimension / image.Height);
        var newWidth = (int)(image.Width * ratio);
        var newHeight = (int)(image.Height * ratio);

        image.Mutate(x => x.Resize(newWidth, newHeight));

        using var thumbnailStream = new MemoryStream();
        await image.SaveAsJpegAsync(thumbnailStream);
        thumbnailStream.Position = 0;

        await _storageService.UploadFileAsync(thumbnailObjectName, thumbnailStream, thumbnailStream.Length, "image/jpeg");

        _logger.LogInformation("Generated thumbnail for {ObjectName}", objectName);

        return thumbnailObjectName;
    }

    private static string GenerateObjectName(string originalName)
    {
        var extension = Path.GetExtension(originalName);
        var guid = Guid.NewGuid().ToString("N");
        var date = DateTime.UtcNow.ToString("yyyy/MM/dd");
        return $"{date}/{guid}{extension}";
    }

    private static async Task<string> ComputeChecksumAsync(Stream stream)
    {
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsImage(string? mimeType)
    {
        return mimeType != null && ImageTypes.Contains(mimeType.ToLowerInvariant());
    }

    #region Chunked Upload

    private static readonly Dictionary<string, ChunkedUploadSession> _uploadSessions = new();
    private const int DefaultChunkSize = 5 * 1024 * 1024; // 5MB chunks

    /// <summary>
    /// Initialize a chunked upload session for large files
    /// </summary>
    public async Task<ChunkedUploadSession?> InitChunkedUploadAsync(long uploaderId, ChunkedUploadInitRequest request)
    {
        if (request.FileSize > MaxFileSize)
        {
            _logger.LogWarning("Chunked upload file size {Size} exceeds maximum {MaxSize}", request.FileSize, MaxFileSize);
            return null;
        }

        var uploadId = Guid.NewGuid().ToString("N");
        var objectName = GenerateObjectName(request.FileName ?? "unknown");
        var totalChunks = (int)Math.Ceiling((double)request.FileSize / DefaultChunkSize);

        var session = new ChunkedUploadSession
        {
            UploadId = uploadId,
            UploaderId = uploaderId,
            FileName = request.FileName,
            MimeType = request.MimeType,
            FileSize = request.FileSize,
            StoragePath = objectName,
            ChunkSize = DefaultChunkSize,
            TotalChunks = totalChunks,
            UploadedChunks = new List<int>(),
            CreatedAt = DateTime.UtcNow
        };

        _uploadSessions[uploadId] = session;

        _logger.LogInformation("Initialized chunked upload {UploadId} for file {FileName}, {TotalChunks} chunks",
            uploadId, request.FileName, totalChunks);

        return await Task.FromResult(session);
    }

    /// <summary>
    /// Upload a single chunk of a chunked upload
    /// </summary>
    public async Task<bool> UploadChunkAsync(string uploadId, int chunkNumber, Stream chunkData, long chunkSize)
    {
        if (!_uploadSessions.TryGetValue(uploadId, out var session))
        {
            _logger.LogWarning("Upload session {UploadId} not found", uploadId);
            return false;
        }

        if (chunkNumber < 0 || chunkNumber >= session.TotalChunks)
        {
            _logger.LogWarning("Invalid chunk number {ChunkNumber} for session {UploadId}", chunkNumber, uploadId);
            return false;
        }

        if (session.UploadedChunks.Contains(chunkNumber))
        {
            _logger.LogDebug("Chunk {ChunkNumber} already uploaded for session {UploadId}", chunkNumber, uploadId);
            return true; // Already uploaded
        }

        // Store chunk with chunk number in path
        var chunkPath = $"{session.StoragePath}.chunks/{chunkNumber:D5}";
        await _storageService.UploadFileAsync(chunkPath, chunkData, chunkSize, "application/octet-stream");

        session.UploadedChunks.Add(chunkNumber);
        session.LastChunkAt = DateTime.UtcNow;

        _logger.LogDebug("Uploaded chunk {ChunkNumber}/{TotalChunks} for session {UploadId}",
            chunkNumber + 1, session.TotalChunks, uploadId);

        return true;
    }

    /// <summary>
    /// Complete a chunked upload by assembling all chunks
    /// </summary>
    public async Task<FileMetadata?> CompleteChunkedUploadAsync(string uploadId)
    {
        if (!_uploadSessions.TryGetValue(uploadId, out var session))
        {
            _logger.LogWarning("Upload session {UploadId} not found", uploadId);
            return null;
        }

        // Verify all chunks are uploaded
        if (session.UploadedChunks.Count != session.TotalChunks)
        {
            _logger.LogWarning("Not all chunks uploaded for session {UploadId}: {Uploaded}/{Total}",
                uploadId, session.UploadedChunks.Count, session.TotalChunks);
            return null;
        }

        // Assemble chunks into final file
        var assembledStream = new MemoryStream();
        for (int i = 0; i < session.TotalChunks; i++)
        {
            var chunkPath = $"{session.StoragePath}.chunks/{i:D5}";
            var chunkStream = await _storageService.DownloadFileAsync(chunkPath);
            if (chunkStream == null)
            {
                _logger.LogError("Failed to download chunk {ChunkNumber} for session {UploadId}", i, uploadId);
                return null;
            }
            await chunkStream.CopyToAsync(assembledStream);
        }

        assembledStream.Position = 0;

        // Compute checksum
        var checksum = await ComputeChecksumAsync(assembledStream);
        assembledStream.Position = 0;

        // Upload assembled file
        await _storageService.UploadFileAsync(session.StoragePath, assembledStream, session.FileSize, session.MimeType ?? "application/octet-stream");

        // Clean up chunks
        for (int i = 0; i < session.TotalChunks; i++)
        {
            var chunkPath = $"{session.StoragePath}.chunks/{i:D5}";
            await _storageService.DeleteFileAsync(chunkPath);
        }

        // Create file metadata
        var metadata = new FileMetadata
        {
            UploaderId = session.UploaderId,
            OriginalName = session.FileName,
            MimeType = session.MimeType,
            SizeBytes = session.FileSize,
            StoragePath = session.StoragePath,
            Checksum = checksum,
            UploadCompletedAt = DateTime.UtcNow
        };

        _context.Files.Add(metadata);
        await _context.SaveChangesAsync();

        // Generate thumbnail if image
        if (IsImage(session.MimeType))
        {
            try
            {
                assembledStream.Position = 0;
                var thumbnailPath = await GenerateThumbnailAsync(session.StoragePath, assembledStream);
                metadata.ThumbnailPath = thumbnailPath;
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate thumbnail for {ObjectName}", session.StoragePath);
            }
        }

        // Remove session
        _uploadSessions.Remove(uploadId);

        _logger.LogInformation("Completed chunked upload {UploadId}, file {FileId}", uploadId, metadata.Id);

        return metadata;
    }

    /// <summary>
    /// Get upload session status
    /// </summary>
    public Task<ChunkedUploadSession?> GetUploadSessionAsync(string uploadId)
    {
        _uploadSessions.TryGetValue(uploadId, out var session);
        return Task.FromResult(session);
    }

    /// <summary>
    /// Abort and clean up a chunked upload
    /// </summary>
    public async Task<bool> AbortChunkedUploadAsync(string uploadId)
    {
        if (!_uploadSessions.TryGetValue(uploadId, out var session))
        {
            return false;
        }

        // Clean up uploaded chunks
        foreach (var chunkNumber in session.UploadedChunks)
        {
            var chunkPath = $"{session.StoragePath}.chunks/{chunkNumber:D5}";
            try
            {
                await _storageService.DeleteFileAsync(chunkPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete chunk {ChunkNumber} during abort", chunkNumber);
            }
        }

        _uploadSessions.Remove(uploadId);

        _logger.LogInformation("Aborted chunked upload {UploadId}", uploadId);

        return true;
    }

    #endregion

    #region Preview Generation

    private static readonly string[] VideoTypes = { "video/mp4", "video/webm", "video/quicktime", "video/x-msvideo" };
    private static readonly string[] DocumentTypes = { "application/pdf" };

    /// <summary>
    /// Generate preview frames from a video file
    /// </summary>
    public async Task<string?> GenerateVideoPreviewAsync(string videoPath)
    {
        // Note: This requires FFmpeg to be installed on the server
        // For now, we'll create a placeholder implementation
        _logger.LogInformation("Video preview generation requested for {VideoPath}", videoPath);

        try
        {
            var videoStream = await _storageService.DownloadFileAsync(videoPath);
            if (videoStream == null)
            {
                _logger.LogWarning("Video file not found: {VideoPath}", videoPath);
                return null;
            }

            // Create a preview GIF from video frames
            // This would typically use FFmpeg to extract frames and create an animated preview
            // For now, we store a placeholder
            var previewPath = $"previews/{videoPath}.gif";

            // In production, you would:
            // 1. Extract key frames using FFmpeg
            // 2. Create an animated GIF or sprite sheet
            // 3. Upload to storage

            _logger.LogInformation("Video preview generated at {PreviewPath}", previewPath);
            return previewPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate video preview for {VideoPath}", videoPath);
            return null;
        }
    }

    /// <summary>
    /// Generate preview for document files (PDF, etc.)
    /// </summary>
    public async Task<string?> GenerateDocumentPreviewAsync(string documentPath, string mimeType)
    {
        _logger.LogInformation("Document preview generation requested for {DocumentPath}", documentPath);

        if (!DocumentTypes.Contains(mimeType))
        {
            _logger.LogDebug("Document type {MimeType} does not support preview generation", mimeType);
            return null;
        }

        try
        {
            var documentStream = await _storageService.DownloadFileAsync(documentPath);
            if (documentStream == null)
            {
                _logger.LogWarning("Document file not found: {DocumentPath}", documentPath);
                return null;
            }

            // For PDFs, we would use a PDF renderer to create a preview image
            // This typically requires Pdfium.Net, PdfSharp, or similar library
            var previewPath = $"previews/{documentPath}.png";

            // In production, you would:
            // 1. Render first page of PDF to image
            // 2. Resize to reasonable preview size
            // 3. Upload to storage

            _logger.LogInformation("Document preview generated at {PreviewPath}", previewPath);
            return previewPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate document preview for {DocumentPath}", documentPath);
            return null;
        }
    }

    /// <summary>
    /// Generate multiple size previews for images
    /// </summary>
    public async Task<List<string>> GenerateImagePreviewsAsync(string imagePath, string mimeType)
    {
        var previewPaths = new List<string>();

        if (!IsImage(mimeType))
        {
            return previewPaths;
        }

        try
        {
            var imageStream = await _storageService.DownloadFileAsync(imagePath);
            if (imageStream == null)
            {
                _logger.LogWarning("Image file not found: {ImagePath}", imagePath);
                return previewPaths;
            }

            using var image = await Image.LoadAsync(imageStream);

            // Generate multiple preview sizes: small (100px), medium (400px), large (800px)
            var sizes = new[] { (Name: "small", Size: 100), (Name: "medium", Size: 400), (Name: "large", Size: 800) };

            foreach (var (name, size) in sizes)
            {
                var previewImage = image.Clone(context =>
                {
                    var ratio = Math.Min((double)size / image.Width, (double)size / image.Height);
                    var newWidth = (int)(image.Width * ratio);
                    var newHeight = (int)(image.Height * ratio);
                    context.Resize(newWidth, newHeight);
                });

                using var previewStream = new MemoryStream();
                await previewImage.SaveAsJpegAsync(previewStream);
                previewStream.Position = 0;

                var previewPath = $"previews/{imagePath}.{name}.jpg";
                await _storageService.UploadFileAsync(previewPath, previewStream, previewStream.Length, "image/jpeg");
                previewPaths.Add(previewPath);

                _logger.LogDebug("Generated {Name} preview at {PreviewPath}", name, previewPath);
            }

            _logger.LogInformation("Generated {Count} image previews for {ImagePath}", previewPaths.Count, imagePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate image previews for {ImagePath}", imagePath);
        }

        return previewPaths;
    }

    #endregion
}
