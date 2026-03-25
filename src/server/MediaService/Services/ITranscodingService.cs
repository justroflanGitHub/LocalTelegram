namespace MediaService.Services;

/// <summary>
/// Service interface for managing transcoding tasks
/// </summary>
public interface ITranscodingService
{
    /// <summary>
    /// Create a new transcoding task
    /// </summary>
    Task<TranscodingTaskModel> CreateTaskAsync(string fileId, string targetQuality, bool generateThumbnail = true, string? callbackUrl = null);
    
    /// <summary>
    /// Get transcoding task by ID
    /// </summary>
    Task<TranscodingTaskModel?> GetTaskAsync(string taskId);
    
    /// <summary>
    /// Get all transcoding tasks for a file
    /// </summary>
    Task<IEnumerable<TranscodingTaskModel>> GetTasksForFileAsync(string fileId);
    
    /// <summary>
    /// Update task status
    /// </summary>
    Task UpdateTaskStatusAsync(string taskId, TranscodingStatus status);
    
    /// <summary>
    /// Complete task with result
    /// </summary>
    Task CompleteTaskAsync(string taskId, TranscodingResultModel result);
    
    /// <summary>
    /// Fail task with error message
    /// </summary>
    Task FailTaskAsync(string taskId, string errorMessage);
    
    /// <summary>
    /// Cancel a pending or processing task
    /// </summary>
    Task CancelTaskAsync(string taskId);
    
    /// <summary>
    /// Get all pending tasks
    /// </summary>
    Task<IEnumerable<TranscodingTaskModel>> GetPendingTasksAsync();
    
    /// <summary>
    /// Queue a transcoding task for processing
    /// </summary>
    Task QueueTaskAsync(TranscodingTaskModel task);
}

/// <summary>
/// Transcoding task model
/// </summary>
public class TranscodingTaskModel
{
    public string TaskId { get; set; } = string.Empty;
    public string FileId { get; set; } = string.Empty;
    public string TargetQuality { get; set; } = "720p";
    public bool GenerateThumbnail { get; set; } = true;
    public string? CallbackUrl { get; set; }
    public TranscodingStatus Status { get; set; } = TranscodingStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public TranscodingResultModel? Result { get; set; }
}

/// <summary>
/// Transcoding status enum
/// </summary>
public enum TranscodingStatus
{
    Pending,
    Queued,
    Processing,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Transcoding result model
/// </summary>
public class TranscodingResultModel
{
    public string? OutputFileId { get; set; }
    public string? OutputUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Duration { get; set; }
    public long Bitrate { get; set; }
    public string? Codec { get; set; }
    public long FileSize { get; set; }
    public long ProcessingTimeMs { get; set; }
}
