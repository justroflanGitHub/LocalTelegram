namespace MediaService.Services;

/// <summary>
/// Service interface for file storage operations
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Upload a file to storage
    /// </summary>
    Task<string> UploadAsync(string fileName, byte[] data, string contentType, string? folder = null);
    
    /// <summary>
    /// Download a file from storage
    /// </summary>
    Task<byte[]> DownloadAsync(string fileId);
    
    /// <summary>
    /// Delete a file from storage
    /// </summary>
    Task DeleteAsync(string fileId);
    
    /// <summary>
    /// Get file metadata
    /// </summary>
    Task<FileMetadata> GetMetadataAsync(string fileId);
    
    /// <summary>
    /// Get a signed URL for direct download
    /// </summary>
    Task<string> GetSignedUrlAsync(string fileId, TimeSpan expiration);
    
    /// <summary>
    /// Check if file exists
    /// </summary>
    Task<bool> ExistsAsync(string fileId);
    
    /// <summary>
    /// Copy file to another location
    /// </summary>
    Task<string> CopyAsync(string fileId, string destinationFolder);
}

/// <summary>
/// File metadata model
/// </summary>
public class FileMetadata
{
    public string FileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? ETag { get; set; }
    public DateTime LastModified { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}
