namespace FileService.Models;

public class FileMetadata
{
    public long Id { get; set; }
    public long UploaderId { get; set; }
    public string? OriginalName { get; set; }
    public string? MimeType { get; set; }
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string StorageProvider { get; set; } = "minio";
    public string? Checksum { get; set; }
    public string? ThumbnailPath { get; set; }
    public bool IsUploaded { get; set; } = true;
    public DateTime? UploadCompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

// DTOs
public class UploadRequest
{
    public IFormFile File { get; set; } = null!;
}

public class UploadResponse
{
    public long FileId { get; set; }
    public string? OriginalName { get; set; }
    public string? MimeType { get; set; }
    public long SizeBytes { get; set; }
    public string? ThumbnailUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class FileDto
{
    public long Id { get; set; }
    public long UploaderId { get; set; }
    public string? OriginalName { get; set; }
    public string? MimeType { get; set; }
    public long SizeBytes { get; set; }
    public string? ThumbnailUrl { get; set; }
    public bool IsUploaded { get; set; }
    public DateTime CreatedAt { get; set; }

    public static FileDto FromMetadata(FileMetadata metadata, string? thumbnailUrl = null)
    {
        return new FileDto
        {
            Id = metadata.Id,
            UploaderId = metadata.UploaderId,
            OriginalName = metadata.OriginalName,
            MimeType = metadata.MimeType,
            SizeBytes = metadata.SizeBytes,
            ThumbnailUrl = thumbnailUrl,
            IsUploaded = metadata.IsUploaded,
            CreatedAt = metadata.CreatedAt
        };
    }
}

public class ChunkedUploadInitRequest
{
    public string? FileName { get; set; }
    public string? MimeType { get; set; }
    public long FileSize { get; set; }
}

public class ChunkedUploadInitResponse
{
    public string UploadId { get; set; } = string.Empty;
    public int ChunkSize { get; set; }
    public int TotalChunks { get; set; }
}

public class UploadChunkRequest
{
    public string UploadId { get; set; } = string.Empty;
    public int ChunkNumber { get; set; }
    public IFormFile Chunk { get; set; } = null!;
}

public class CompleteUploadRequest
{
    public string UploadId { get; set; } = string.Empty;
}

/// <summary>
/// Session for chunked file upload
/// </summary>
public class ChunkedUploadSession
{
    public string UploadId { get; set; } = string.Empty;
    public long UploaderId { get; set; }
    public string? FileName { get; set; }
    public string? MimeType { get; set; }
    public long FileSize { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public int ChunkSize { get; set; }
    public int TotalChunks { get; set; }
    public List<int> UploadedChunks { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? LastChunkAt { get; set; }
}

/// <summary>
/// Upload chunk status response
/// </summary>
public class ChunkUploadStatus
{
    public string UploadId { get; set; } = string.Empty;
    public int TotalChunks { get; set; }
    public int UploadedChunks { get; set; }
    public List<int> MissingChunks { get; set; } = new();
    public bool IsComplete => MissingChunks.Count == 0;
}

/// <summary>
/// Preview metadata for files
/// </summary>
public class FilePreview
{
    public long FileId { get; set; }
    public string? SmallPreviewUrl { get; set; }
    public string? MediumPreviewUrl { get; set; }
    public string? LargePreviewUrl { get; set; }
    public string? VideoPreviewUrl { get; set; }
    public string? DocumentPreviewUrl { get; set; }
}
