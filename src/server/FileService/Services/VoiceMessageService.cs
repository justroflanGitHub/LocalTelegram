using System.Diagnostics;
using System.Security.Cryptography;
using FileService.Data;
using FileService.Models;
using Microsoft.EntityFrameworkCore;

namespace FileService.Services;

public interface IVoiceMessageService
{
    Task<VoiceMessage?> UploadVoiceMessageAsync(long uploaderId, IFormFile audioFile, int durationSeconds, string? waveform = null);
    Task<VoiceMessage?> GetVoiceMessageAsync(long voiceMessageId);
    Task<Stream?> DownloadVoiceMessageAsync(long voiceMessageId);
    Task<string?> GetVoiceMessageUrlAsync(long voiceMessageId);
    Task<bool> DeleteVoiceMessageAsync(long voiceMessageId, long userId);
    Task<string> GenerateWaveformAsync(Stream audioData);
    Task<Stream?> TranscodeToOpusAsync(Stream inputData, string inputFormat);
}

public class VoiceMessageService : IVoiceMessageService
{
    private readonly FileDbContext _context;
    private readonly IStorageService _storageService;
    private readonly ILogger<VoiceMessageService> _logger;
    private readonly IConfiguration _configuration;
    
    private const int MaxVoiceMessageSize = 100 * 1024 * 1024; // 100MB
    private const int MaxDurationSeconds = 3600; // 1 hour
    private const int WaveformSampleCount = 100; // Number of waveform samples
    
    private static readonly string[] SupportedInputFormats = { 
        "audio/ogg", "audio/opus", "audio/mpeg", "audio/mp3", 
        "audio/wav", "audio/x-wav", "audio/aac", "audio/mp4",
        "audio/webm", "audio/x-m4a"
    };

    public VoiceMessageService(
        FileDbContext context,
        IStorageService storageService,
        ILogger<VoiceMessageService> logger,
        IConfiguration configuration)
    {
        _context = context;
        _storageService = storageService;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<VoiceMessage?> UploadVoiceMessageAsync(
        long uploaderId, 
        IFormFile audioFile, 
        int durationSeconds,
        string? waveform = null)
    {
        // Validate file size
        if (audioFile.Length > MaxVoiceMessageSize)
        {
            _logger.LogWarning("Voice message size {Size} exceeds maximum {MaxSize}", 
                audioFile.Length, MaxVoiceMessageSize);
            return null;
        }

        // Validate duration
        if (durationSeconds <= 0 || durationSeconds > MaxDurationSeconds)
        {
            _logger.LogWarning("Invalid duration: {Duration} seconds", durationSeconds);
            return null;
        }

        // Validate content type
        var contentType = audioFile.ContentType.ToLower();
        if (!SupportedInputFormats.Any(f => contentType.Contains(f.Split('/').Last())))
        {
            _logger.LogWarning("Unsupported audio format: {ContentType}", contentType);
            return null;
        }

        string objectName;
        string? checksum;
        Stream? finalStream = null;
        bool needsTranscoding = !IsOpusFormat(contentType);

        try
        {
            if (needsTranscoding)
            {
                // Transcode to Opus
                using var inputStream = audioFile.OpenReadStream();
                finalStream = await TranscodeToOpusAsync(inputStream, contentType);
                
                if (finalStream == null)
                {
                    _logger.LogWarning("Failed to transcode voice message");
                    return null;
                }
                
                objectName = GenerateObjectName("voice.ogg");
                checksum = await ComputeChecksumAsync(finalStream);
                finalStream.Position = 0;
                
                await _storageService.UploadFileAsync(objectName, finalStream, finalStream.Length, "audio/ogg");
            }
            else
            {
                // Already in Opus/Ogg format
                objectName = GenerateObjectName(audioFile.FileName);
                using var stream = audioFile.OpenReadStream();
                checksum = await ComputeChecksumAsync(stream);
                stream.Position = 0;
                
                await _storageService.UploadFileAsync(objectName, stream, audioFile.Length, contentType);
            }

            // Generate waveform if not provided
            string finalWaveform;
            if (string.IsNullOrEmpty(waveform))
            {
                if (finalStream != null)
                {
                    finalStream.Position = 0;
                    finalWaveform = await GenerateWaveformAsync(finalStream);
                }
                else
                {
                    using var stream = audioFile.OpenReadStream();
                    finalWaveform = await GenerateWaveformAsync(stream);
                }
            }
            else
            {
                finalWaveform = waveform;
            }

            // Create file metadata
            var fileMetadata = new FileMetadata
            {
                UploaderId = uploaderId,
                OriginalName = audioFile.FileName,
                MimeType = "audio/ogg",
                SizeBytes = finalStream?.Length ?? audioFile.Length,
                StoragePath = objectName,
                Checksum = checksum,
                UploadCompletedAt = DateTime.UtcNow
            };

            _context.Files.Add(fileMetadata);
            await _context.SaveChangesAsync();

            // Create voice message record
            var voiceMessage = new VoiceMessage
            {
                FileId = fileMetadata.Id,
                DurationSeconds = durationSeconds,
                Waveform = finalWaveform,
                Codec = "opus",
                SampleRate = 48000,
                Channels = 1,
                Bitrate = 64000,
                CreatedAt = DateTime.UtcNow
            };

            _context.VoiceMessages.Add(voiceMessage);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Voice message {VoiceId} uploaded by user {UserId}", 
                voiceMessage.Id, uploaderId);

            return voiceMessage;
        }
        finally
        {
            finalStream?.Dispose();
        }
    }

    public async Task<VoiceMessage?> GetVoiceMessageAsync(long voiceMessageId)
    {
        return await _context.VoiceMessages
            .Include(v => v.File)
            .FirstOrDefaultAsync(v => v.Id == voiceMessageId);
    }

    public async Task<Stream?> DownloadVoiceMessageAsync(long voiceMessageId)
    {
        var voiceMessage = await GetVoiceMessageAsync(voiceMessageId);
        if (voiceMessage?.File == null)
        {
            return null;
        }

        return await _storageService.DownloadFileAsync(voiceMessage.File.StoragePath);
    }

    public async Task<string?> GetVoiceMessageUrlAsync(long voiceMessageId)
    {
        var voiceMessage = await GetVoiceMessageAsync(voiceMessageId);
        if (voiceMessage?.File == null)
        {
            return null;
        }

        return await _storageService.GetPresignedUrlAsync(voiceMessage.File.StoragePath);
    }

    public async Task<bool> DeleteVoiceMessageAsync(long voiceMessageId, long userId)
    {
        var voiceMessage = await GetVoiceMessageAsync(voiceMessageId);
        if (voiceMessage?.File == null)
        {
            return false;
        }

        // Verify ownership
        if (voiceMessage.File.UploaderId != userId)
        {
            _logger.LogWarning("User {UserId} attempted to delete voice message {VoiceId} owned by {OwnerId}",
                userId, voiceMessageId, voiceMessage.File.UploaderId);
            return false;
        }

        // Delete from storage
        await _storageService.DeleteFileAsync(voiceMessage.File.StoragePath);

        // Delete from database
        _context.VoiceMessages.Remove(voiceMessage);
        _context.Files.Remove(voiceMessage.File);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Voice message {VoiceId} deleted by user {UserId}", 
            voiceMessageId, userId);

        return true;
    }

    public async Task<string> GenerateWaveformAsync(Stream audioData)
    {
        // Generate waveform samples using FFmpeg
        var samples = await ExtractWaveformSamplesAsync(audioData);
        
        // Normalize and compress to base64
        var waveform = new List<int>();
        foreach (var sample in samples)
        {
            // Normalize to 0-31 range (5 bits per sample for compact storage)
            var normalized = Math.Min(31, (int)(sample * 31 / 32768));
            waveform.Add(normalized);
        }

        // Convert to base64
        var bytes = waveform.Select(v => (byte)v).ToArray();
        return Convert.ToBase64String(bytes);
    }

    public async Task<Stream?> TranscodeToOpusAsync(Stream inputData, string inputFormat)
    {
        var ffmpegPath = _configuration["FFmpeg:Path"] ?? "ffmpeg";
        
        // Create temporary files
        var tempInput = Path.Combine(Path.GetTempPath(), $"voice_input_{Guid.NewGuid()}.tmp");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"voice_output_{Guid.NewGuid()}.ogg");

        try
        {
            // Write input to temp file
            using (var fileStream = File.Create(tempInput))
            {
                await inputData.CopyToAsync(fileStream);
            }

            // FFmpeg arguments for Opus encoding
            var arguments = $"-i \"{tempInput}\" -c:a libopus -b:a 64k -vbr on -compression_level 10 \"{tempOutput}\"";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();
            
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                _logger.LogError("FFmpeg transcoding failed: {Error}", error);
                return null;
            }

            // Read output file
            if (!File.Exists(tempOutput))
            {
                _logger.LogError("FFmpeg output file not created");
                return null;
            }

            var outputStream = new MemoryStream();
            using (var fileStream = File.OpenRead(tempOutput))
            {
                await fileStream.CopyToAsync(outputStream);
            }
            
            outputStream.Position = 0;
            return outputStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during voice transcoding");
            return null;
        }
        finally
        {
            // Cleanup temp files
            try
            {
                if (File.Exists(tempInput)) File.Delete(tempInput);
                if (File.Exists(tempOutput)) File.Delete(tempOutput);
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    private async Task<List<short>> ExtractWaveformSamplesAsync(Stream audioData)
    {
        var ffmpegPath = _configuration["FFmpeg:Path"] ?? "ffmpeg";
        var samples = new List<short>();

        var tempInput = Path.Combine(Path.GetTempPath(), $"waveform_input_{Guid.NewGuid()}.tmp");

        try
        {
            // Write input to temp file
            using (var fileStream = File.Create(tempInput))
            {
                await audioData.CopyToAsync(fileStream);
            }

            // FFmpeg arguments to extract waveform data
            var arguments = $"-i \"{tempInput}\" -ac 1 -filter:a aresample={WaveformSampleCount} -f s16le -";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            // Read raw PCM data
            using var outputStream = process.StandardOutput.BaseStream;
            var buffer = new byte[2]; // 16-bit samples
            int bytesRead;

            while ((bytesRead = await outputStream.ReadAsync(buffer, 0, 2)) == 2)
            {
                samples.Add(BitConverter.ToInt16(buffer, 0));
            }

            await process.WaitForExitAsync();

            return samples;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting waveform samples");
            return samples;
        }
        finally
        {
            try
            {
                if (File.Exists(tempInput)) File.Delete(tempInput);
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    private static bool IsOpusFormat(string contentType)
    {
        return contentType.Contains("opus") || contentType.Contains("ogg") || contentType.Contains("webm");
    }

    private static string GenerateObjectName(string originalName)
    {
        var extension = Path.GetExtension(originalName);
        return $"voices/{Guid.NewGuid()}{extension}";
    }

    private static async Task<string> ComputeChecksumAsync(Stream stream)
    {
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
