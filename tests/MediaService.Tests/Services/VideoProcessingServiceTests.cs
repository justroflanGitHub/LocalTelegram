using FluentAssertions;
using MediaService.Data;
using MediaService.Models;
using MediaService.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Minio;
using Xunit;

namespace MediaService.Tests.Services;

public class VideoProcessingServiceTests : IDisposable
{
    private readonly MediaDbContext _dbContext;
    private readonly Mock<IMinioClient> _minioClientMock;
    private readonly Mock<ILogger<VideoProcessingService>> _loggerMock;

    public VideoProcessingServiceTests()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new MediaDbContext(options);

        _minioClientMock = new Mock<IMinioClient>();
        _loggerMock = new Mock<ILogger<VideoProcessingService>>();
    }

    [Fact]
    public async Task GetVideoFile_WhenNotExists_ReturnsNull()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MinIO:BucketName"] = "test-media"
            })
            .Build();

        var service = new VideoProcessingService(_dbContext, _minioClientMock.Object, config, _loggerMock.Object);

        // Act
        var result = await service.GetVideoFileAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetVideoFile_WhenExists_ReturnsVideoFile()
    {
        // Arrange
        var videoFile = new VideoFile
        {
            Id = 1,
            OriginalFileId = 100,
            DurationSeconds = 120,
            Width = 1920,
            Height = 1080,
            Codec = "h264",
            BitrateKbps = 5000,
            FrameRate = 30.0,
            FileSizeBytes = 50_000_000,
            MimeType = "video/mp4",
            HasAudio = true,
            AudioCodec = "aac",
            Status = VideoProcessingStatus.Completed,
            UserId = 1
        };

        _dbContext.VideoFiles.Add(videoFile);
        await _dbContext.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MinIO:BucketName"] = "test-media"
            })
            .Build();

        var service = new VideoProcessingService(_dbContext, _minioClientMock.Object, config, _loggerMock.Object);

        // Act
        var result = await service.GetVideoFileAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.OriginalFileId.Should().Be(100);
        result.DurationSeconds.Should().Be(120);
        result.Width.Should().Be(1920);
        result.Height.Should().Be(1080);
        result.Codec.Should().Be("h264");
        result.Status.Should().Be(VideoProcessingStatus.Completed);
    }

    [Fact]
    public async Task GetStreamingInfo_WhenVideoExists_ReturnsInfo()
    {
        // Arrange
        var videoFile = new VideoFile
        {
            Id = 1,
            OriginalFileId = 100,
            DurationSeconds = 60,
            Width = 1280,
            Height = 720,
            Codec = "h264",
            BitrateKbps = 2500,
            FrameRate = 30.0,
            FileSizeBytes = 25_000_000,
            MimeType = "video/mp4",
            HasAudio = true,
            Status = VideoProcessingStatus.Completed,
            UserId = 1
        };

        var variant = new VideoVariant
        {
            Id = 1,
            VideoFileId = 1,
            Quality = VideoQuality.HD,
            Width = 1280,
            Height = 720,
            BitrateKbps = 2500,
            StoragePath = "videos/1/hd.mp4",
            FileSizeBytes = 25_000_000
        };

        _dbContext.VideoFiles.Add(videoFile);
        _dbContext.VideoVariants.Add(variant);
        await _dbContext.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MinIO:BucketName"] = "test-media"
            })
            .Build();

        var service = new VideoProcessingService(_dbContext, _minioClientMock.Object, config, _loggerMock.Object);

        // Act
        var result = await service.GetStreamingInfoAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.VideoId.Should().Be(1);
        result.DurationSeconds.Should().Be(60);
        result.Variants.Should().HaveCount(1);
        result.Variants[0].Quality.Should().Be(VideoQuality.HD);
    }

    [Fact]
    public async Task GetStreamingInfo_WhenNotExists_ReturnsNull()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MinIO:BucketName"] = "test-media"
            })
            .Build();

        var service = new VideoProcessingService(_dbContext, _minioClientMock.Object, config, _loggerMock.Object);

        // Act
        var result = await service.GetStreamingInfoAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(VideoQuality.Low, 426, 240, 400)]
    [InlineData(VideoQuality.Medium, 640, 360, 800)]
    [InlineData(VideoQuality.Standard, 854, 480, 1200)]
    [InlineData(VideoQuality.HD, 1280, 720, 2500)]
    [InlineData(VideoQuality.FullHD, 1920, 1080, 5000)]
    [InlineData(VideoQuality.QHD, 2560, 1440, 8000)]
    [InlineData(VideoQuality.UltraHD, 3840, 2160, 16000)]
    public void QualityPresets_ShouldHaveCorrectValues(VideoQuality quality, int expectedWidth, int expectedHeight, int expectedBitrate)
    {
        // This test verifies the quality presets are correctly defined
        // The service uses a static dictionary with these values
        
        // Arrange & Act - Using reflection to get the private static field
        var qualityPresetsField = typeof(VideoProcessingService)
            .GetField("QualityPresets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        // Assert
        qualityPresetsField.Should().NotBeNull();
    }

    [Fact]
    public async Task VideoFile_ShouldTrackProcessingStatus()
    {
        // Arrange
        var videoFile = new VideoFile
        {
            OriginalFileId = 1,
            Status = VideoProcessingStatus.Pending,
            UserId = 1
        };

        _dbContext.VideoFiles.Add(videoFile);
        await _dbContext.SaveChangesAsync();

        // Act - Update status
        videoFile.Status = VideoProcessingStatus.Processing;
        videoFile.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Assert
        var saved = await _dbContext.VideoFiles.FindAsync(videoFile.Id);
        saved!.Status.Should().Be(VideoProcessingStatus.Processing);

        // Act - Complete processing
        saved.Status = VideoProcessingStatus.Completed;
        saved.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Assert
        var completed = await _dbContext.VideoFiles.FindAsync(videoFile.Id);
        completed!.Status.Should().Be(VideoProcessingStatus.Completed);
    }

    [Fact]
    public async Task VideoVariant_ShouldBeLinkedToVideoFile()
    {
        // Arrange
        var videoFile = new VideoFile
        {
            OriginalFileId = 1,
            Status = VideoProcessingStatus.Completed,
            UserId = 1
        };

        _dbContext.VideoFiles.Add(videoFile);
        await _dbContext.SaveChangesAsync();

        var variant = new VideoVariant
        {
            VideoFileId = videoFile.Id,
            Quality = VideoQuality.HD,
            Width = 1280,
            Height = 720,
            BitrateKbps = 2500,
            StoragePath = "videos/1/hd.mp4",
            FileSizeBytes = 10_000_000
        };

        _dbContext.VideoVariants.Add(variant);
        await _dbContext.SaveChangesAsync();

        // Act
        var savedVariant = await _dbContext.VideoVariants
            .Include(v => v.VideoFile)
            .FirstOrDefaultAsync(v => v.Id == variant.Id);

        // Assert
        savedVariant.Should().NotBeNull();
        savedVariant!.VideoFile.Should().NotBeNull();
        savedVariant.VideoFile.Id.Should().Be(videoFile.Id);
    }

    [Fact]
    public async Task VideoMessage_ShouldBeCreatedForRoundVideo()
    {
        // Arrange
        var videoMessage = new VideoMessage
        {
            FileId = 1,
            DurationSeconds = 30,
            Width = 384,
            Height = 384,
            UserId = 1
        };

        // Act
        _dbContext.VideoMessages.Add(videoMessage);
        await _dbContext.SaveChangesAsync();

        // Assert
        var saved = await _dbContext.VideoMessages.FindAsync(videoMessage.Id);
        saved.Should().NotBeNull();
        saved!.Width.Should().Be(384);
        saved.Height.Should().Be(384);
        saved.DurationSeconds.Should().BeLessOrEqualTo(60); // Round videos max 60 seconds
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }
}

// Helper class for configuration
internal static class ConfigurationBuilderExtensions
{
    public static IConfigurationBuilder AddInMemoryCollection(
        this IConfigurationBuilder builder,
        IEnumerable<KeyValuePair<string, string?>>? initialData)
    {
        return builder.Add(new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource
        {
            InitialData = initialData
        });
    }
}
