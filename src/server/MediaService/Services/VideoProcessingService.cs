using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaService.Data;
using MediaService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;

namespace MediaService.Services;

/// <summary>
/// Service for video processing, transcoding, and streaming
/// </summary>
public interface IVideoProcessingService
{
    /// <summary>
    /// Extract metadata from video file
    /// </summary>
    Task<VideoMetadata> ExtractMetadataAsync(Stream videoData, string fileName);
    
    /// <summary>
    /// Transcode video to specified format and qualities
    /// </summary>
    Task<VideoFile> TranscodeVideoAsync(long fileId, Stream videoData, VideoTranscodeOptions options);
    
    /// <summary>
    /// Generate thumbnail from video at specified timestamp
    /// </summary>
    Task<Stream> GenerateThumbnailAsync(Stream videoData, int width = 320, int height = 240, int timestampSeconds = 0);
    
    /// <summary>
    /// Generate animated GIF preview from video
    /// </summary>
    Task<Stream> GenerateAnimatedPreviewAsync(Stream videoData, int width = 320, int durationSeconds = 5, int fps = 10);
    
    /// <summary>
    /// Create video variants for adaptive streaming
    /// </summary>
    Task<List<VideoVariant>> CreateVariantsAsync(long videoFileId, List<VideoQuality> qualities);
    
    /// <summary>
    /// Get streaming URL for video variant
    /// </summary>
    Task<string> GetStreamingUrlAsync(long videoId, VideoQuality quality);
    
    /// <summary>
    /// Process round video message (max 60 seconds, 384x384)
    /// </summary>
    Task<VideoMessage> ProcessRoundVideoAsync(long fileId, Stream videoData, long userId);
    
    /// <summary>
    /// Get video file by ID
    /// </summary>
    Task<VideoFile?> GetVideoFileAsync(long videoId);
    
    /// <summary>
    /// Get video streaming info
    /// </summary>
    Task<VideoStreamingInfo?> GetStreamingInfoAsync(long videoId);
}

public class VideoProcessingService : IVideoProcessingService
{
    private readonly MediaDbContext _dbContext;
    private readonly IMinioClient _minioClient;
    private readonly ILogger<VideoProcessingService> _logger;
    private readonly string _bucketName;
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly string _tempPath;

    // Quality presets (width, height, max bitrate kbps)
    private static readonly Dictionary<VideoQuality, (int Width, int Height, int Bitrate)> QualityPresets = new()
    {
        { VideoQuality.Low, (426, 240, 400) },
        { VideoQuality.Medium, (640, 360, 800) },
        { VideoQuality.Standard, (854, 480, 1200) },
        { VideoQuality.HD, (1280, 720, 2500) },
        { VideoQuality.FullHD, (1920, 1080, 5000) },
        { VideoQuality.QHD, (2560, 1440, 8000) },
        { VideoQuality.UltraHD, (3840, 2160, 16000) }
    };

    public VideoProcessingService(
        MediaDbContext dbContext,
        IMinioClient minioClient,
        IConfiguration configuration,
        ILogger<VideoProcessingService> logger)
    {
        _dbContext = dbContext;
        _minioClient = minioClient;
        _logger = logger;
        _bucketName = configuration["MinIO:BucketName"] ?? "media";
        _ffmpegPath = configuration["FFmpeg:Path"] ?? "ffmpeg";
        _ffprobePath = configuration["FFprobe:Path"] ?? "ffprobe";
        _tempPath = configuration["TempPath"] ?? Path.Combine(Path.GetTempPath(), "media_processing");
        
        Directory.CreateDirectory(_tempPath);
    }

    public async Task<VideoMetadata> ExtractMetadataAsync(Stream videoData, string fileName)
    {
        var tempFile = Path.Combine(_tempPath, $"meta_{Guid.NewGuid()}{Path.GetExtension(fileName)}");
        
        try
        {
            // Save to temp file
            await using (var fs = File.Create(tempFile))
            {
                videoData.Seek(0, SeekOrigin.Begin);
                await videoData.CopyToAsync(fs);
            }

            var metadata = await RunFfprobeAsync(tempFile);
            return metadata;
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    public async Task<VideoFile> TranscodeVideoAsync(long fileId, Stream videoData, VideoTranscodeOptions options)
    {
        var tempInput = Path.Combine(_tempPath, $"input_{Guid.NewGuid()}.mp4");
        var tempOutput = Path.Combine(_tempPath, $"output_{Guid.NewGuid()}.mp4");
        
        try
        {
            // Save input to temp file
            await using (var fs = File.Create(tempInput))
            {
                videoData.Seek(0, SeekOrigin.Begin);
                await videoData.CopyToAsync(fs);
            }

            // Extract metadata first
            var metadata = await RunFfprobeAsync(tempInput);
            
            // Create video file record
            var videoFile = new VideoFile
            {
                OriginalFileId = fileId,
                DurationSeconds = metadata.DurationSeconds,
                Width = metadata.Width,
                Height = metadata.Height,
                Codec = "h264",
                BitrateKbps = options.MaxBitrateKbps ?? metadata.BitrateKbps,
                FrameRate = metadata.FrameRate,
                FileSizeBytes = metadata.FileSizeBytes,
                MimeType = "video/mp4",
                HasAudio = metadata.HasAudio,
                AudioCodec = metadata.AudioCodec,
                Status = VideoProcessingStatus.Processing,
                UserId = options.UserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.VideoFiles.Add(videoFile);
            await _dbContext.SaveChangesAsync();

            // Build FFmpeg arguments
            var outputWidth = options.MaxWidth ?? metadata.Width;
            var outputHeight = options.MaxHeight ?? metadata.Height;
            var outputBitrate = options.MaxBitrateKbps ?? metadata.BitrateKbps;

            var ffmpegArgs = BuildTranscodeArgs(tempInput, tempOutput, outputWidth, outputHeight, outputBitrate, options.OutputFormat);

            // Run transcoding
            await RunFfmpegAsync(ffmpegArgs);

            // Upload transcoded video to storage
            var storagePath = $"videos/{videoFile.Id}/original.mp4";
            await using (var outputStream = File.OpenRead(tempOutput))
            {
                await UploadToStorageAsync(storagePath, outputStream, "video/mp4");
            }

            // Update video file
            var outputInfo = new FileInfo(tempOutput);
            videoFile.FileSizeBytes = outputInfo.Length;
            videoFile.Status = VideoProcessingStatus.Completed;
            videoFile.UpdatedAt = DateTime.UtcNow;
            
            await _dbContext.SaveChangesAsync();

            return videoFile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transcode video file {FileId}", fileId);
            throw;
        }
        finally
        {
            CleanupTempFiles(tempInput, tempOutput);
        }
    }

    public async Task<Stream> GenerateThumbnailAsync(Stream videoData, int width = 320, int height = 240, int timestampSeconds = 0)
    {
        var tempInput = Path.Combine(_tempPath, $"thumb_input_{Guid.NewGuid()}.mp4");
        var tempOutput = Path.Combine(_tempPath, $"thumb_output_{Guid.NewGuid()}.jpg");
        
        try
        {
            // Save input to temp file
            await using (var fs = File.Create(tempInput))
            {
                videoData.Seek(0, SeekOrigin.Begin);
                await videoData.CopyToAsync(fs);
            }

            // Generate thumbnail using FFmpeg
            var args = $"-i \"{tempInput}\" -ss {timestampSeconds} -vframes 1 -vf \"scale={width}:{height}:force_original_aspect_ratio=decrease\" -q:v 2 \"{tempOutput}\"";
            await RunFfmpegAsync(args);

            // Read thumbnail
            var thumbnailData = await File.ReadAllBytesAsync(tempOutput);
            return new MemoryStream(thumbnailData);
        }
        finally
        {
            CleanupTempFiles(tempInput, tempOutput);
        }
    }

    public async Task<Stream> GenerateAnimatedPreviewAsync(Stream videoData, int width = 320, int durationSeconds = 5, int fps = 10)
    {
        var tempInput = Path.Combine(_tempPath, $"gif_input_{Guid.NewGuid()}.mp4");
        var tempOutput = Path.Combine(_tempPath, $"gif_output_{Guid.NewGuid()}.gif");
        
        try
        {
            // Save input to temp file
            await using (var fs = File.Create(tempInput))
            {
                videoData.Seek(0, SeekOrigin.Begin);
                await videoData.CopyToAsync(fs);
            }

            // Generate GIF using FFmpeg with palette for better quality
            var palettePath = Path.Combine(_tempPath, $"palette_{Guid.NewGuid()}.png");
            
            // Generate palette first
            var paletteArgs = $"-i \"{tempInput}\" -vf \"fps={fps},scale={width}:-1:flags=lanczos,palettegen\" -t {durationSeconds} \"{palettePath}\"";
            await RunFfmpegAsync(paletteArgs);

            // Generate GIF with palette
            var gifArgs = $"-i \"{tempInput}\" -i \"{palettePath}\" -lavfi \"fps={fps},scale={width}:-1:flags=lanczos[x];[x][1:v]paletteuse\" -t {durationSeconds} \"{tempOutput}\"";
            await RunFfmpegAsync(gifArgs);

            // Read GIF
            var gifData = await File.ReadAllBytesAsync(tempOutput);
            return new MemoryStream(gifData);
        }
        finally
        {
            CleanupTempFiles(tempInput, tempOutput);
        }
    }

    public async Task<List<VideoVariant>> CreateVariantsAsync(long videoFileId, List<VideoQuality> qualities)
    {
        var videoFile = await _dbContext.VideoFiles.FindAsync(videoFileId);
        if (videoFile == null)
            throw new InvalidOperationException($"Video file {videoFileId} not found");

        var variants = new List<VideoVariant>();
        
        // Download original video from storage
        var originalPath = $"videos/{videoFileId}/original.mp4";
        var tempInput = Path.Combine(_tempPath, $"variant_input_{Guid.NewGuid()}.mp4");
        
        try
        {
            await DownloadFromStorageAsync(originalPath, tempInput);

            foreach (var quality in qualities)
            {
                var preset = QualityPresets[quality];
                
                // Skip if source is smaller than target
                if (videoFile.Width < preset.Width && videoFile.Height < preset.Height)
                    continue;

                var tempOutput = Path.Combine(_tempPath, $"variant_{videoFileId}_{quality}.mp4");
                
                try
                {
                    // Transcode to this quality
                    var args = BuildTranscodeArgs(tempInput, tempOutput, preset.Width, preset.Height, preset.Bitrate, "mp4");
                    await RunFfmpegAsync(args);

                    // Upload to storage
                    var variantPath = $"videos/{videoFileId}/{quality}.mp4";
                    await using (var outputStream = File.OpenRead(tempOutput))
                    {
                        await UploadToStorageAsync(variantPath, outputStream, "video/mp4");
                    }

                    // Create variant record
                    var variant = new VideoVariant
                    {
                        VideoFileId = videoFileId,
                        Quality = quality,
                        Width = preset.Width,
                        Height = preset.Height,
                        BitrateKbps = preset.Bitrate,
                        StoragePath = variantPath,
                        FileSizeBytes = new FileInfo(tempOutput).Length
                    };

                    variants.Add(variant);
                    _dbContext.VideoVariants.Add(variant);
                }
                finally
                {
                    if (File.Exists(tempOutput))
                        File.Delete(tempOutput);
                }
            }

            await _dbContext.SaveChangesAsync();
            return variants;
        }
        finally
        {
            if (File.Exists(tempInput))
                File.Delete(tempInput);
        }
    }

    public async Task<string> GetStreamingUrlAsync(long videoId, VideoQuality quality)
    {
        var variant = await _dbContext.VideoVariants
            .FirstOrDefaultAsync(v => v.VideoFileId == videoId && v.Quality == quality);

        if (variant == null)
            throw new InvalidOperationException($"Variant not found for video {videoId} quality {quality}");

        // Generate presigned URL (valid for 1 hour)
        var presignedUrl = await _minioClient.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(variant.StoragePath)
            .WithExpiry(3600));

        return presignedUrl;
    }

    public async Task<VideoMessage> ProcessRoundVideoAsync(long fileId, Stream videoData, long userId)
    {
        var tempInput = Path.Combine(_tempPath, $"round_input_{Guid.NewGuid()}.mp4");
        var tempOutput = Path.Combine(_tempPath, $"round_output_{Guid.NewGuid()}.mp4");
        var tempGif = Path.Combine(_tempPath, $"round_gif_{Guid.NewGuid()}.gif");
        
        try
        {
            // Save input to temp file
            await using (var fs = File.Create(tempInput))
            {
                videoData.Seek(0, SeekOrigin.Begin);
                await videoData.CopyToAsync(fs);
            }

            // Get metadata
            var metadata = await RunFfprobeAsync(tempInput);
            
            // Validate duration (max 60 seconds for round video)
            if (metadata.DurationSeconds > 60)
                throw new ArgumentException("Round video duration cannot exceed 60 seconds");

            // Transcode to 384x384 with circular crop effect
            // Using h264 with moderate bitrate for round video
            var args = $"-i \"{tempInput}\" " +
                       $"-vf \"scale=384:384:force_original_aspect_ratio=increase,crop=384:384\" " +
                       $"-c:v libx264 -preset fast -crf 28 -maxrate 800k -bufsize 1600k " +
                       $"-c:a aac -b:a 64k " +
                       $"-t 60 -movflags +faststart \"{tempOutput}\"";
            
            await RunFfmpegAsync(args);

            // Generate animated preview
            var palettePath = Path.Combine(_tempPath, $"palette_{Guid.NewGuid()}.png");
            var paletteArgs = $"-i \"{tempOutput}\" -vf \"fps=10,scale=192:-1:flags=lanczos,palettegen\" -t 3 \"{palettePath}\"";
            await RunFfmpegAsync(paletteArgs);
            
            var gifArgs = $"-i \"{tempOutput}\" -i \"{palettePath}\" -lavfi \"fps=10,scale=192:-1:flags=lanczos[x];[x][1:v]paletteuse\" -t 3 \"{tempGif}\"";
            await RunFfmpegAsync(gifArgs);

            // Upload video and GIF to storage
            var videoPath = $"round_videos/{fileId}/video.mp4";
            var gifPath = $"round_videos/{fileId}/preview.gif";
            
            await using (var videoStream = File.OpenRead(tempOutput))
            {
                await UploadToStorageAsync(videoPath, videoStream, "video/mp4");
            }
            
            long? animatedThumbnailId = null;
            await using (var gifStream = File.OpenRead(tempGif))
            {
                // Store GIF as thumbnail (would typically reference FileService)
                animatedThumbnailId = fileId; // Placeholder
            }

            // Create video message record
            var videoMessage = new VideoMessage
            {
                FileId = fileId,
                DurationSeconds = metadata.DurationSeconds,
                Width = 384,
                Height = 384,
                AnimatedThumbnailId = animatedThumbnailId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.VideoMessages.Add(videoMessage);
            await _dbContext.SaveChangesAsync();

            return videoMessage;
        }
        finally
        {
            CleanupTempFiles(tempInput, tempOutput, tempGif);
        }
    }

    public async Task<VideoFile?> GetVideoFileAsync(long videoId)
    {
        return await _dbContext.VideoFiles
            .Include(v => v.VideoFile)
            .FirstOrDefaultAsync(v => v.Id == videoId);
    }

    public async Task<VideoStreamingInfo?> GetStreamingInfoAsync(long videoId)
    {
        var videoFile = await GetVideoFileAsync(videoId);
        if (videoFile == null)
            return null;

        var variants = await _dbContext.VideoVariants
            .Where(v => v.VideoFileId == videoId)
            .ToListAsync();

        var streamingInfo = new VideoStreamingInfo
        {
            VideoId = videoId,
            DurationSeconds = videoFile.DurationSeconds,
            ThumbnailUrl = videoFile.ThumbnailFileId.HasValue 
                ? $"/api/media/{videoId}/thumbnail" 
                : null
        };

        foreach (var variant in variants)
        {
            var url = await GetStreamingUrlAsync(videoId, variant.Quality);
            streamingInfo.Variants.Add(new VideoVariantInfo
            {
                Quality = variant.Quality,
                Width = variant.Width,
                Height = variant.Height,
                BitrateKbps = variant.BitrateKbps,
                StreamingUrl = url,
                FileSizeBytes = variant.FileSizeBytes
            });
        }

        return streamingInfo;
    }

    #region Private Methods

    private async Task<VideoMetadata> RunFfprobeAsync(string filePath)
    {
        var args = $"-v quiet -print_format json -show_format -show_streams \"{filePath}\"";
        
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ffprobePath,
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

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"FFprobe failed: {error}");
        }

        return ParseFfprobeOutput(output);
    }

    private VideoMetadata ParseFfprobeOutput(string jsonOutput)
    {
        using var doc = JsonDocument.Parse(jsonOutput);
        var root = doc.RootElement;

        var metadata = new VideoMetadata();

        // Get format info
        if (root.TryGetProperty("format", out var format))
        {
            if (format.TryGetProperty("duration", out var duration))
                metadata.DurationSeconds = (int)double.Parse(duration.GetString() ?? "0");
            
            if (format.TryGetProperty("bit_rate", out var bitRate))
                metadata.BitrateKbps = (int)(double.Parse(bitRate.GetString() ?? "0") / 1000);
            
            if (format.TryGetProperty("size", out var size))
                metadata.FileSizeBytes = long.Parse(size.GetString() ?? "0");
        }

        // Get stream info
        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var codecType = stream.GetProperty("codec_type").GetString();
                
                if (codecType == "video")
                {
                    metadata.Width = stream.GetProperty("width").GetInt32();
                    metadata.Height = stream.GetProperty("height").GetInt32();
                    metadata.Codec = stream.GetProperty("codec_name").GetString() ?? "unknown";
                    
                    // Frame rate
                    if (stream.TryGetProperty("r_frame_rate", out var frameRate))
                    {
                        var fpsStr = frameRate.GetString() ?? "0/1";
                        var parts = fpsStr.Split('/');
                        if (parts.Length == 2 && int.TryParse(parts[1], out var denom) && denom > 0)
                        {
                            metadata.FrameRate = double.Parse(parts[0]) / denom;
                        }
                    }
                    
                    // Rotation
                    if (stream.TryGetProperty("side_data_list", out var sideData))
                    {
                        foreach (var sd in sideData.EnumerateArray())
                        {
                            if (sd.TryGetProperty("rotation", out var rotation))
                            {
                                metadata.Rotation = rotation.GetInt32().ToString();
                            }
                        }
                    }
                }
                else if (codecType == "audio")
                {
                    metadata.HasAudio = true;
                    metadata.AudioCodec = stream.GetProperty("codec_name").GetString();
                    
                    if (stream.TryGetProperty("sample_rate", out var sampleRate))
                        metadata.AudioSampleRate = sampleRate.GetInt32();
                    
                    if (stream.TryGetProperty("channels", out var channels))
                        metadata.AudioChannels = channels.GetInt32();
                }
            }
        }

        return metadata;
    }

    private string BuildTranscodeArgs(string input, string output, int width, int height, int bitrateKbps, string format)
    {
        var scaleFilter = $"scale={width}:{height}:force_original_aspect_ratio=decrease";
        
        return $"-i \"{input}\" " +
               $"-vf \"{scaleFilter}\" " +
               $"-c:v libx264 -preset fast -crf 23 -maxrate {bitrateKbps}k -bufsize {bitrateKbps * 2}k " +
               $"-c:a aac -b:a 128k " +
               $"-movflags +faststart " +
               $"-f {format} \"{output}\"";
    }

    private async Task RunFfmpegAsync(string args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        
        // Read stderr for progress (FFmpeg outputs progress to stderr)
        var errorOutput = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _logger.LogError("FFmpeg failed with exit code {ExitCode}: {Error}", process.ExitCode, errorOutput);
            throw new InvalidOperationException($"FFmpeg transcoding failed: {errorOutput}");
        }
    }

    private async Task UploadToStorageAsync(string path, Stream data, string contentType)
    {
        // Ensure bucket exists
        var bucketExists = await _minioClient.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(_bucketName));
        
        if (!bucketExists)
        {
            await _minioClient.MakeBucketAsync(new MakeBucketArgs()
                .WithBucket(_bucketName));
        }

        await _minioClient.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(path)
            .WithStreamData(data)
            .WithObjectSize(data.Length)
            .WithContentType(contentType));
    }

    private async Task DownloadFromStorageAsync(string path, string localPath)
    {
        await _minioClient.GetObjectAsync(new GetObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(path)
            .WithFile(localPath));
    }

    private void CleanupTempFiles(params string[] files)
    {
        foreach (var file in files)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup temp file: {File}", file);
            }
        }
    }

    #endregion
}

/// <summary>
/// Options for video transcoding
/// </summary>
public class VideoTranscodeOptions
{
    public long UserId { get; set; }
    public string OutputFormat { get; set; } = "mp4";
    public int? MaxWidth { get; set; }
    public int? MaxHeight { get; set; }
    public int? MaxBitrateKbps { get; set; }
    public bool GenerateThumbnail { get; set; } = true;
    public bool GenerateVariants { get; set; } = true;
    public List<VideoQuality> Qualities { get; set; } = new() { VideoQuality.Medium, VideoQuality.Standard, VideoQuality.HD };
}
