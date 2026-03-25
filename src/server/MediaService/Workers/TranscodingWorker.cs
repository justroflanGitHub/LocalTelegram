using System.Diagnostics;
using System.Text.Json;
using MediaService.Models;
using MediaService.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace MediaService.Workers;

/// <summary>
/// Background worker for processing video transcoding tasks from RabbitMQ
/// </summary>
public class TranscodingWorker : BackgroundService
{
    private readonly ILogger<TranscodingWorker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly string _queueName = "transcoding_tasks";
    private readonly string _exchangeName = "media_exchange";
    
    // Retry configuration
    private const int MaxRetries = 3;
    private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _maxRetryDelay = TimeSpan.FromMinutes(5);
    
    public TranscodingWorker(
        ILogger<TranscodingWorker> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        
        var factory = new ConnectionFactory
        {
            HostName = configuration["RabbitMQ:Host"] ?? "localhost",
            Port = int.Parse(configuration["RabbitMQ:Port"] ?? "5672"),
            UserName = configuration["RabbitMQ:User"] ?? "guest",
            Password = configuration["RabbitMQ:Password"] ?? "guest",
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
        };
        
        try
        {
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            
            // Declare exchange and queue
            _channel.ExchangeDeclare(_exchangeName, ExchangeType.Direct, durable: true);
            _channel.QueueDeclare(_queueName, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(_queueName, _exchangeName, "transcode");
            
            // Declare retry queue
            _channel.QueueDeclare($"{_queueName}_retry", durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind($"{_queueName}_retry", _exchangeName, "transcode_retry");
            
            // Declare dead letter queue
            _channel.QueueDeclare($"{_queueName}_dead", durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind($"{_queueName}_dead", _exchangeName, "transcode_dead");
            
            _logger.LogInformation("Transcoding worker connected to RabbitMQ");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to RabbitMQ");
            throw;
        }
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Transcoding worker started");
        
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = JsonSerializer.Deserialize<TranscodingTask>(body);
            
            if (message == null)
            {
                _logger.LogWarning("Failed to deserialize transcoding task");
                _channel.BasicNack(ea.DeliveryTag, false, false);
                return;
            }
            
            _logger.LogInformation("Received transcoding task: {TaskId} for file {FileId}", 
                message.TaskId, message.FileId);
            
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var transcodingService = scope.ServiceProvider.GetRequiredService<ITranscodingService>();
                var fileStorageService = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
                
                // Update task status
                await transcodingService.UpdateTaskStatusAsync(message.TaskId, TranscodingStatus.Processing);
                
                // Process the transcoding task
                var result = await ProcessTranscodingTaskAsync(message, transcodingService, fileStorageService);
                
                if (result.Success)
                {
                    // Update task with result
                    await transcodingService.CompleteTaskAsync(message.TaskId, result);
                    
                    // Acknowledge message
                    _channel.BasicAck(ea.DeliveryTag, false);
                    _logger.LogInformation("Transcoding task completed: {TaskId}", message.TaskId);
                }
                else
                {
                    throw new Exception(result.ErrorMessage ?? "Transcoding failed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transcoding task failed: {TaskId}", message.TaskId);
                
                // Handle retry logic
                var retryCount = GetRetryCount(ea);
                
                if (retryCount < MaxRetries)
                {
                    // Schedule retry with exponential backoff
                    var delay = CalculateRetryDelay(retryCount);
                    await ScheduleRetryAsync(message, retryCount + 1, delay);
                    
                    // Acknowledge original message (we've scheduled a retry)
                    _channel.BasicAck(ea.DeliveryTag, false);
                    
                    _logger.LogWarning("Scheduled retry {RetryCount} for task {TaskId} after {Delay}s", 
                        retryCount + 1, message.TaskId, delay.TotalSeconds);
                }
                else
                {
                    // Max retries exceeded, send to dead letter queue
                    await SendToDeadLetterQueueAsync(message, ex.Message);
                    _channel.BasicAck(ea.DeliveryTag, false);
                    
                    _logger.LogError("Task {TaskId} failed after {MaxRetries} retries, sent to dead letter queue", 
                        message.TaskId, MaxRetries);
                    
                    // Update task status
                    using var scope = _serviceProvider.CreateScope();
                    var transcodingService = scope.ServiceProvider.GetRequiredService<ITranscodingService>();
                    await transcodingService.FailTaskAsync(message.TaskId, ex.Message);
                }
            }
        };
        
        _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);
        
        // Keep the worker running
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
        
        _logger.LogInformation("Transcoding worker stopped");
    }
    
    private async Task<TranscodingResult> ProcessTranscodingTaskAsync(
        TranscodingTask task,
        ITranscodingService transcodingService,
        IFileStorageService fileStorageService)
    {
        var tempInputPath = Path.Combine(Path.GetTempPath(), $"transcode_input_{task.FileId}");
        var tempOutputPath = Path.Combine(Path.GetTempPath(), $"transcode_output_{task.FileId}");
        
        try
        {
            // Download input file from storage
            _logger.LogInformation("Downloading input file: {FileId}", task.FileId);
            var inputBytes = await fileStorageService.DownloadAsync(task.FileId);
            await File.WriteAllBytesAsync(tempInputPath, inputBytes);
            
            // Get video info
            var videoInfo = await GetVideoInfoAsync(tempInputPath);
            _logger.LogInformation("Input video: {Width}x{Height}, {Duration}s, {Codec}", 
                videoInfo.Width, videoInfo.Height, videoInfo.Duration, videoInfo.Codec);
            
            // Determine output parameters based on target quality
            var outputParams = GetOutputParameters(task.TargetQuality, videoInfo);
            
            // Build FFmpeg command
            var ffmpegArgs = BuildFFmpegArgs(tempInputPath, tempOutputPath, outputParams, task);
            
            // Run FFmpeg
            var stopwatch = Stopwatch.StartNew();
            var success = await RunFFmpegAsync(ffmpegArgs, task.TaskId);
            stopwatch.Stop();
            
            if (!success)
            {
                return new TranscodingResult
                {
                    Success = false,
                    ErrorMessage = "FFmpeg transcoding failed"
                };
            }
            
            // Upload output file
            var outputBytes = await File.ReadAllBytesAsync(tempOutputPath);
            var outputFileName = $"{task.FileId}_{task.TargetQuality}.{outputParams.Extension}";
            var outputUrl = await fileStorageService.UploadAsync(
                outputFileName, 
                outputBytes, 
                $"video/{outputParams.Extension}");
            
            // Generate thumbnail if requested
            string? thumbnailUrl = null;
            if (task.GenerateThumbnail)
            {
                thumbnailUrl = await GenerateThumbnailAsync(tempInputPath, task.FileId, fileStorageService);
            }
            
            // Get output video info
            var outputInfo = await GetVideoInfoAsync(tempOutputPath);
            
            return new TranscodingResult
            {
                Success = true,
                OutputFileId = $"{task.FileId}_{task.TargetQuality}",
                OutputUrl = outputUrl,
                ThumbnailUrl = thumbnailUrl,
                Width = outputInfo.Width,
                Height = outputInfo.Height,
                Duration = outputInfo.Duration,
                Bitrate = outputInfo.Bitrate,
                Codec = outputParams.VideoCodec,
                FileSize = outputBytes.Length,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        finally
        {
            // Cleanup temp files
            if (File.Exists(tempInputPath)) File.Delete(tempInputPath);
            if (File.Exists(tempOutputPath)) File.Delete(tempOutputPath);
        }
    }
    
    private async Task<VideoInfo> GetVideoInfoAsync(string filePath)
    {
        var args = $"-v quiet -print_format json -show_format -show_streams \"{filePath}\"";
        
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        
        // Parse JSON output
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        
        var format = root.GetProperty("format");
        var streams = root.GetProperty("streams");
        
        var videoStream = streams.EnumerateArray()
            .FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video");
        
        return new VideoInfo
        {
            Width = videoStream.GetProperty("width").GetInt32(),
            Height = videoStream.GetProperty("height").GetInt32(),
            Duration = double.Parse(format.GetProperty("duration").GetString() ?? "0"),
            Bitrate = format.TryGetProperty("bit_rate", out var bitrate) 
                ? long.Parse(bitrate.GetString() ?? "0") 
                : 0,
            Codec = videoStream.GetProperty("codec_name").GetString() ?? "unknown"
        };
    }
    
    private OutputParameters GetOutputParameters(string quality, VideoInfo inputInfo)
    {
        return quality.ToLower() switch
        {
            "4k" => new OutputParameters
            {
                Width = 3840,
                Height = 2160,
                VideoBitrate = "15000k",
                AudioBitrate = "192k",
                VideoCodec = "libx264",
                AudioCodec = "aac",
                Extension = "mp4",
                Preset = "medium"
            },
            "1080p" => new OutputParameters
            {
                Width = 1920,
                Height = 1080,
                VideoBitrate = "5000k",
                AudioBitrate = "128k",
                VideoCodec = "libx264",
                AudioCodec = "aac",
                Extension = "mp4",
                Preset = "medium"
            },
            "720p" => new OutputParameters
            {
                Width = 1280,
                Height = 720,
                VideoBitrate = "2500k",
                AudioBitrate = "128k",
                VideoCodec = "libx264",
                AudioCodec = "aac",
                Extension = "mp4",
                Preset = "fast"
            },
            "480p" => new OutputParameters
            {
                Width = 854,
                Height = 480,
                VideoBitrate = "1000k",
                AudioBitrate = "96k",
                VideoCodec = "libx264",
                AudioCodec = "aac",
                Extension = "mp4",
                Preset = "fast"
            },
            "360p" => new OutputParameters
            {
                Width = 640,
                Height = 360,
                VideoBitrate = "600k",
                AudioBitrate = "64k",
                VideoCodec = "libx264",
                AudioCodec = "aac",
                Extension = "mp4",
                Preset = "fast"
            },
            "webm" => new OutputParameters
            {
                Width = Math.Min(inputInfo.Width, 1280),
                Height = Math.Min(inputInfo.Height, 720),
                VideoBitrate = "2000k",
                AudioBitrate = "128k",
                VideoCodec = "libvpx-vp9",
                AudioCodec = "libopus",
                Extension = "webm",
                Preset = "medium"
            },
            _ => new OutputParameters
            {
                Width = Math.Min(inputInfo.Width, 1280),
                Height = Math.Min(inputInfo.Height, 720),
                VideoBitrate = "2500k",
                AudioBitrate = "128k",
                VideoCodec = "libx264",
                AudioCodec = "aac",
                Extension = "mp4",
                Preset = "medium"
            }
        };
    }
    
    private string BuildFFmpegArgs(string inputPath, string outputPath, OutputParameters params, TranscodingTask task)
    {
        var args = $"-i \"{inputPath}\" " +
                   $"-c:v {params.VideoCodec} " +
                   $"-c:a {params.AudioCodec} " +
                   $"-b:v {params.VideoBitrate} " +
                   $"-b:a {params.AudioBitrate} " +
                   $"-s {params.Width}x{params.Height} " +
                   $"-preset {params.Preset} " +
                   $"-movflags +faststart " +
                   $"-y \"{outputPath}\"";
        
        return args;
    }
    
    private async Task<bool> RunFFmpegAsync(string args, string taskId)
    {
        _logger.LogInformation("Running FFmpeg for task {TaskId}: {Args}", taskId, args);
        
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogDebug("FFmpeg [{TaskId}]: {Data}", taskId, e.Data);
            }
        };
        
        process.Start();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        
        return process.ExitCode == 0;
    }
    
    private async Task<string> GenerateThumbnailAsync(string videoPath, string fileId, IFileStorageService storageService)
    {
        var thumbnailPath = Path.Combine(Path.GetTempPath(), $"thumb_{fileId}.jpg");
        
        try
        {
            var args = $"-i \"{videoPath}\" -ss 00:00:01 -vframes 1 -q:v 2 -y \"{thumbnailPath}\"";
            
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            await process.WaitForExitAsync();
            
            if (process.ExitCode != 0 || !File.Exists(thumbnailPath))
            {
                return string.Empty;
            }
            
            var thumbnailBytes = await File.ReadAllBytesAsync(thumbnailPath);
            var thumbnailUrl = await storageService.UploadAsync(
                $"thumbnails/{fileId}.jpg",
                thumbnailBytes,
                "image/jpeg");
            
            return thumbnailUrl;
        }
        finally
        {
            if (File.Exists(thumbnailPath)) File.Delete(thumbnailPath);
        }
    }
    
    private int GetRetryCount(BasicDeliverEventArgs ea)
    {
        if (ea.BasicProperties.Headers != null && 
            ea.BasicProperties.Headers.TryGetValue("x-retry-count", out var retryCountObj))
        {
            return Convert.ToInt32(retryCountObj);
        }
        return 0;
    }
    
    private TimeSpan CalculateRetryDelay(int retryCount)
    {
        // Exponential backoff with jitter
        var delay = TimeSpan.FromSeconds(Math.Min(
            _retryDelay.TotalSeconds * Math.Pow(2, retryCount),
            _maxRetryDelay.TotalSeconds));
        
        // Add jitter (±10%)
        var jitter = delay.TotalSeconds * 0.1 * (Random.Shared.NextDouble() * 2 - 1);
        return delay + TimeSpan.FromSeconds(jitter);
    }
    
    private async Task ScheduleRetryAsync(TranscodingTask task, int retryCount, TimeSpan delay)
    {
        await Task.Delay(delay);
        
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.Headers = new Dictionary<string, object>
        {
            { "x-retry-count", retryCount },
            { "x-original-task-id", task.TaskId }
        };
        
        var body = JsonSerializer.SerializeToBytes(task);
        _channel.BasicPublish(_exchangeName, "transcode", properties, body);
    }
    
    private async Task SendToDeadLetterQueueAsync(TranscodingTask task, string errorMessage)
    {
        var deadLetterMessage = new DeadLetterMessage
        {
            OriginalTask = task,
            ErrorMessage = errorMessage,
            FailedAt = DateTime.UtcNow,
            RetryCount = MaxRetries
        };
        
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        
        var body = JsonSerializer.SerializeToBytes(deadLetterMessage);
        _channel.BasicPublish(_exchangeName, "transcode_dead", properties, body);
        
        await Task.CompletedTask;
    }
    
    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}

/// <summary>
/// Transcoding task message
/// </summary>
public class TranscodingTask
{
    public string TaskId { get; set; } = string.Empty;
    public string FileId { get; set; } = string.Empty;
    public string TargetQuality { get; set; } = "720p";
    public bool GenerateThumbnail { get; set; } = true;
    public string? CallbackUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Transcoding result
/// </summary>
public class TranscodingResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
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

/// <summary>
/// Video information
/// </summary>
public class VideoInfo
{
    public int Width { get; set; }
    public int Height { get; set; }
    public double Duration { get; set; }
    public long Bitrate { get; set; }
    public string Codec { get; set; } = string.Empty;
}

/// <summary>
/// Output parameters for transcoding
/// </summary>
public class OutputParameters
{
    public int Width { get; set; }
    public int Height { get; set; }
    public string VideoBitrate { get; set; } = string.Empty;
    public string AudioBitrate { get; set; } = string.Empty;
    public string VideoCodec { get; set; } = string.Empty;
    public string AudioCodec { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string Preset { get; set; } = string.Empty;
}

/// <summary>
/// Dead letter message for failed tasks
/// </summary>
public class DeadLetterMessage
{
    public TranscodingTask OriginalTask { get; set; } = null!;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime FailedAt { get; set; }
    public int RetryCount { get; set; }
}
