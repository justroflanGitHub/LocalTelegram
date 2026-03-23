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
}
