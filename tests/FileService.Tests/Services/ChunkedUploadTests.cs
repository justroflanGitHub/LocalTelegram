using FluentAssertions;
using FileService.Data;
using FileService.Models;
using FileService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FileService.Tests.Services;

/// <summary>
/// Tests for FileService chunked upload and preview features
/// </summary>
public class ChunkedUploadTests : IDisposable
{
    private readonly FileDbContext _context;
    private readonly Mock<IStorageService> _storageServiceMock;
    private readonly Mock<ILogger<FileService.Services.FileService>> _loggerMock;
    private readonly FileService.Services.FileService _service;

    public ChunkedUploadTests()
    {
        var options = new DbContextOptionsBuilder<FileDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new FileDbContext(options);
        _storageServiceMock = new Mock<IStorageService>();
        _loggerMock = new Mock<ILogger<FileService.Services.FileService>>();

        _service = new FileService.Services.FileService(
            _context,
            _storageServiceMock.Object,
            _loggerMock.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    #region Chunked Upload Tests

    [Fact]
    public async Task InitChunkedUploadAsync_ShouldCreateSession_WhenValidRequest()
    {
        // Arrange
        var request = new ChunkedUploadInitRequest
        {
            FileName = "test.mp4",
            MimeType = "video/mp4",
            FileSize = 50 * 1024 * 1024 // 50MB
        };

        // Act
        var result = await _service.InitChunkedUploadAsync(1, request);

        // Assert
        result.Should().NotBeNull();
        result!.UploadId.Should().NotBeNullOrEmpty();
        result.ChunkSize.Should().Be(5 * 1024 * 1024); // 5MB default
        result.TotalChunks.Should().Be(10); // 50MB / 5MB = 10 chunks
        result.UploaderId.Should().Be(1);
        result.FileName.Should().Be("test.mp4");
    }

    [Fact]
    public async Task InitChunkedUploadAsync_ShouldReturnNull_WhenFileSizeExceedsLimit()
    {
        // Arrange
        var request = new ChunkedUploadInitRequest
        {
            FileName = "huge.mp4",
            MimeType = "video/mp4",
            FileSize = 3L * 1024 * 1024 * 1024 // 3GB (exceeds 2GB limit)
        };

        // Act
        var result = await _service.InitChunkedUploadAsync(1, request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task InitChunkedUploadAsync_ShouldCalculateCorrectChunkCount()
    {
        // Arrange - 12.5 MB file should result in 3 chunks (5MB + 5MB + 2.5MB)
        var request = new ChunkedUploadInitRequest
        {
            FileName = "test.bin",
            MimeType = "application/octet-stream",
            FileSize = (long)(12.5 * 1024 * 1024)
        };

        // Act
        var result = await _service.InitChunkedUploadAsync(1, request);

        // Assert
        result.Should().NotBeNull();
        result!.TotalChunks.Should().Be(3);
    }

    [Fact]
    public async Task UploadChunkAsync_ShouldUploadChunk_WhenValidRequest()
    {
        // Arrange
        var session = await _service.InitChunkedUploadAsync(1, new ChunkedUploadInitRequest
        {
            FileName = "test.mp4",
            MimeType = "video/mp4",
            FileSize = 15 * 1024 * 1024
        });

        using var chunkData = new MemoryStream(new byte[1024]);
        _storageServiceMock.Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.UploadChunkAsync(session!.UploadId, 0, chunkData, 1024);

        // Assert
        result.Should().BeTrue();
        _storageServiceMock.Verify(x => x.UploadFileAsync(
            It.Is<string>(s => s.Contains("chunks") && s.Contains("00000")),
            It.IsAny<Stream>(),
            1024,
            "application/octet-stream"), Times.Once);
    }

    [Fact]
    public async Task UploadChunkAsync_ShouldReturnFalse_WhenSessionNotFound()
    {
        // Arrange
        using var chunkData = new MemoryStream(new byte[1024]);

        // Act
        var result = await _service.UploadChunkAsync("nonexistent", 0, chunkData, 1024);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UploadChunkAsync_ShouldReturnFalse_WhenChunkNumberInvalid()
    {
        // Arrange
        var session = await _service.InitChunkedUploadAsync(1, new ChunkedUploadInitRequest
        {
            FileName = "test.mp4",
            MimeType = "video/mp4",
            FileSize = 10 * 1024 * 1024 // 2 chunks
        });

        using var chunkData = new MemoryStream(new byte[1024]);

        // Act - Try to upload chunk 5 when only 0 and 1 are valid
        var result = await _service.UploadChunkAsync(session!.UploadId, 5, chunkData, 1024);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UploadChunkAsync_ShouldAllowDuplicateChunk_WhenAlreadyUploaded()
    {
        // Arrange
        var session = await _service.InitChunkedUploadAsync(1, new ChunkedUploadInitRequest
        {
            FileName = "test.mp4",
            MimeType = "video/mp4",
            FileSize = 10 * 1024 * 1024
        });

        using var chunkData = new MemoryStream(new byte[1024]);
        _storageServiceMock.Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Upload chunk 0
        await _service.UploadChunkAsync(session!.UploadId, 0, chunkData, 1024);

        // Act - Upload same chunk again
        chunkData.Position = 0;
        var result = await _service.UploadChunkAsync(session.UploadId, 0, chunkData, 1024);

        // Assert
        result.Should().BeTrue(); // Should succeed but not upload again
        _storageServiceMock.Verify(x => x.UploadFileAsync(
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<string>()),
            Times.Once()); // Only called once
    }

    [Fact]
    public async Task GetUploadSessionAsync_ShouldReturnSession_WhenExists()
    {
        // Arrange
        var session = await _service.InitChunkedUploadAsync(1, new ChunkedUploadInitRequest
        {
            FileName = "test.mp4",
            MimeType = "video/mp4",
            FileSize = 10 * 1024 * 1024
        });

        // Act
        var result = await _service.GetUploadSessionAsync(session!.UploadId);

        // Assert
        result.Should().NotBeNull();
        result!.UploadId.Should().Be(session.UploadId);
    }

    [Fact]
    public async Task GetUploadSessionAsync_ShouldReturnNull_WhenNotExists()
    {
        // Act
        var result = await _service.GetUploadSessionAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task AbortChunkedUploadAsync_ShouldRemoveSessionAndCleanup()
    {
        // Arrange
        var session = await _service.InitChunkedUploadAsync(1, new ChunkedUploadInitRequest
        {
            FileName = "test.mp4",
            MimeType = "video/mp4",
            FileSize = 10 * 1024 * 1024
        });

        _storageServiceMock.Setup(x => x.DeleteFileAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.AbortChunkedUploadAsync(session!.UploadId);

        // Assert
        result.Should().BeTrue();
        
        var deletedSession = await _service.GetUploadSessionAsync(session.UploadId);
        deletedSession.Should().BeNull();
    }

    [Fact]
    public async Task CompleteChunkedUploadAsync_ShouldReturnNull_WhenNotAllChunksUploaded()
    {
        // Arrange
        var session = await _service.InitChunkedUploadAsync(1, new ChunkedUploadInitRequest
        {
            FileName = "test.mp4",
            MimeType = "video/mp4",
            FileSize = 15 * 1024 * 1024 // 3 chunks
        });

        using var chunkData = new MemoryStream(new byte[1024]);
        _storageServiceMock.Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Upload only 2 of 3 chunks
        await _service.UploadChunkAsync(session!.UploadId, 0, chunkData, 1024);
        chunkData.Position = 0;
        await _service.UploadChunkAsync(session.UploadId, 1, chunkData, 1024);

        // Act
        var result = await _service.CompleteChunkedUploadAsync(session.UploadId);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Preview Generation Tests

    [Fact]
    public async Task GenerateImagePreviewsAsync_ShouldReturnEmpty_WhenNotImage()
    {
        // Arrange
        _storageServiceMock.Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
            .ReturnsAsync(new MemoryStream());

        // Act
        var result = await _service.GenerateImagePreviewsAsync("test.pdf", "application/pdf");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateImagePreviewsAsync_ShouldReturnEmpty_WhenFileNotFound()
    {
        // Arrange
        _storageServiceMock.Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
            .ReturnsAsync((Stream?)null);

        // Act
        var result = await _service.GenerateImagePreviewsAsync("test.jpg", "image/jpeg");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateVideoPreviewAsync_ShouldReturnNull_WhenFileNotFound()
    {
        // Arrange
        _storageServiceMock.Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
            .ReturnsAsync((Stream?)null);

        // Act
        var result = await _service.GenerateVideoPreviewAsync("test.mp4");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GenerateDocumentPreviewAsync_ShouldReturnNull_WhenNotSupportedType()
    {
        // Arrange
        _storageServiceMock.Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
            .ReturnsAsync(new MemoryStream());

        // Act
        var result = await _service.GenerateDocumentPreviewAsync("test.docx", "application/docx");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GenerateDocumentPreviewAsync_ShouldReturnNull_WhenFileNotFound()
    {
        // Arrange
        _storageServiceMock.Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
            .ReturnsAsync((Stream?)null);

        // Act
        var result = await _service.GenerateDocumentPreviewAsync("test.pdf", "application/pdf");

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
