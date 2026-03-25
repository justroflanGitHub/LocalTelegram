using FileService.Data;
using FileService.Models;
using FileService.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FileService.Tests.Services;

public class VoiceMessageServiceTests : IDisposable
{
    private readonly FileDbContext _context;
    private readonly Mock<IStorageService> _storageServiceMock;
    private readonly Mock<ILogger<VoiceMessageService>> _loggerMock;
    private readonly Mock<IConfiguration> _configurationMock;

    public VoiceMessageServiceTests()
    {
        var options = new DbContextOptionsBuilder<FileDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new FileDbContext(options);
        _storageServiceMock = new Mock<IStorageService>();
        _loggerMock = new Mock<ILogger<VoiceMessageService>>();
        _configurationMock = new Mock<IConfiguration>();

        _configurationMock.Setup(c => c["FFmpeg:Path"]).Returns("ffmpeg");
    }

    [Fact]
    public async Task GetVoiceMessageAsync_WhenNotExists_ReturnsNull()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetVoiceMessageAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetVoiceMessageAsync_WhenExists_ReturnsVoiceMessage()
    {
        // Arrange
        var file = new FileMetadata
        {
            UploaderId = 1,
            OriginalName = "test.ogg",
            MimeType = "audio/ogg",
            SizeBytes = 1000,
            StoragePath = "voices/test.ogg"
        };
        _context.Files.Add(file);
        await _context.SaveChangesAsync();

        var voiceMessage = new VoiceMessage
        {
            FileId = file.Id,
            DurationSeconds = 10,
            Waveform = "AAAA",
            Codec = "opus"
        };
        _context.VoiceMessages.Add(voiceMessage);
        await _context.SaveChangesAsync();

        var service = CreateService();

        // Act
        var result = await service.GetVoiceMessageAsync(voiceMessage.Id);

        // Assert
        result.Should().NotBeNull();
        result!.DurationSeconds.Should().Be(10);
        result.Waveform.Should().Be("AAAA");
        result.File.Should().NotBeNull();
    }

    [Fact]
    public async Task GetVoiceMessageUrlAsync_WhenNotExists_ReturnsNull()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetVoiceMessageUrlAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetVoiceMessageUrlAsync_WhenExists_ReturnsUrl()
    {
        // Arrange
        var file = new FileMetadata
        {
            UploaderId = 1,
            OriginalName = "test.ogg",
            MimeType = "audio/ogg",
            SizeBytes = 1000,
            StoragePath = "voices/test.ogg"
        };
        _context.Files.Add(file);
        await _context.SaveChangesAsync();

        var voiceMessage = new VoiceMessage
        {
            FileId = file.Id,
            DurationSeconds = 10,
            Waveform = "AAAA"
        };
        _context.VoiceMessages.Add(voiceMessage);
        await _context.SaveChangesAsync();

        _storageServiceMock
            .Setup(s => s.GetPresignedUrlAsync("voices/test.ogg"))
            .ReturnsAsync("https://storage.example.com/voices/test.ogg");

        var service = CreateService();

        // Act
        var result = await service.GetVoiceMessageUrlAsync(voiceMessage.Id);

        // Assert
        result.Should().Be("https://storage.example.com/voices/test.ogg");
    }

    [Fact]
    public async Task DeleteVoiceMessageAsync_WhenNotExists_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.DeleteVoiceMessageAsync(999, 1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteVoiceMessageAsync_WhenNotOwner_ReturnsFalse()
    {
        // Arrange
        var file = new FileMetadata
        {
            UploaderId = 1,
            OriginalName = "test.ogg",
            MimeType = "audio/ogg",
            SizeBytes = 1000,
            StoragePath = "voices/test.ogg"
        };
        _context.Files.Add(file);
        await _context.SaveChangesAsync();

        var voiceMessage = new VoiceMessage
        {
            FileId = file.Id,
            DurationSeconds = 10,
            Waveform = "AAAA"
        };
        _context.VoiceMessages.Add(voiceMessage);
        await _context.SaveChangesAsync();

        var service = CreateService();

        // Act
        var result = await service.DeleteVoiceMessageAsync(voiceMessage.Id, 2); // Different user

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteVoiceMessageAsync_WhenOwner_DeletesSuccessfully()
    {
        // Arrange
        var file = new FileMetadata
        {
            UploaderId = 1,
            OriginalName = "test.ogg",
            MimeType = "audio/ogg",
            SizeBytes = 1000,
            StoragePath = "voices/test.ogg"
        };
        _context.Files.Add(file);
        await _context.SaveChangesAsync();

        var voiceMessage = new VoiceMessage
        {
            FileId = file.Id,
            DurationSeconds = 10,
            Waveform = "AAAA"
        };
        _context.VoiceMessages.Add(voiceMessage);
        await _context.SaveChangesAsync();

        _storageServiceMock
            .Setup(s => s.DeleteFileAsync("voices/test.ogg"))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        var result = await service.DeleteVoiceMessageAsync(voiceMessage.Id, 1);

        // Assert
        result.Should().BeTrue();
        _storageServiceMock.Verify(s => s.DeleteFileAsync("voices/test.ogg"), Times.Once);

        var deletedVoice = await _context.VoiceMessages.FindAsync(voiceMessage.Id);
        deletedVoice.Should().BeNull();

        var deletedFile = await _context.Files.FindAsync(file.Id);
        deletedFile.Should().BeNull();
    }

    [Fact]
    public async Task DownloadVoiceMessageAsync_WhenNotExists_ReturnsNull()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.DownloadVoiceMessageAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DownloadVoiceMessageAsync_WhenExists_ReturnsStream()
    {
        // Arrange
        var file = new FileMetadata
        {
            UploaderId = 1,
            OriginalName = "test.ogg",
            MimeType = "audio/ogg",
            SizeBytes = 1000,
            StoragePath = "voices/test.ogg"
        };
        _context.Files.Add(file);
        await _context.SaveChangesAsync();

        var voiceMessage = new VoiceMessage
        {
            FileId = file.Id,
            DurationSeconds = 10,
            Waveform = "AAAA"
        };
        _context.VoiceMessages.Add(voiceMessage);
        await _context.SaveChangesAsync();

        var audioData = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        _storageServiceMock
            .Setup(s => s.DownloadFileAsync("voices/test.ogg"))
            .ReturnsAsync(audioData);

        var service = CreateService();

        // Act
        var result = await service.DownloadVoiceMessageAsync(voiceMessage.Id);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GenerateWaveformAsync_ReturnsBase64String()
    {
        // Arrange
        var service = CreateService();
        var audioData = new MemoryStream(new byte[] { 0, 0, 255, 255, 128, 128 });

        // Act
        var result = await service.GenerateWaveformAsync(audioData);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // Should be valid base64
        var bytes = Convert.FromBase64String(result);
        bytes.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3601)] // Over 1 hour
    public async Task UploadVoiceMessageAsync_InvalidDuration_ReturnsNull(int duration)
    {
        // Arrange
        var service = CreateService();
        var file = CreateMockFormFile("test.mp3", "audio/mpeg", 1000);

        // Act
        var result = await service.UploadVoiceMessageAsync(1, file.Object, duration);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UploadVoiceMessageAsync_FileTooLarge_ReturnsNull()
    {
        // Arrange
        var service = CreateService();
        var file = CreateMockFormFile("test.mp3", "audio/mpeg", 101 * 1024 * 1024); // 101MB

        // Act
        var result = await service.UploadVoiceMessageAsync(1, file.Object, 10);

        // Assert
        result.Should().BeNull();
    }

    private IVoiceMessageService CreateService()
    {
        return new VoiceMessageService(
            _context,
            _storageServiceMock.Object,
            _loggerMock.Object,
            _configurationMock.Object);
    }

    private static Mock<IFormFile> CreateMockFormFile(string fileName, string contentType, long length)
    {
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.ContentType).Returns(contentType);
        fileMock.Setup(f => f.Length).Returns(length);
        fileMock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[length > int.MaxValue ? 1000 : (int)length]));
        return fileMock;
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
