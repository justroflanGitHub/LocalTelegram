using System.IO;
using FluentAssertions;
using FileService.Data;
using FileService.Models;
using FileService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FileService.Tests.Services;

public class FileServiceTests : IDisposable
{
    private readonly FileDbContext _context;
    private readonly Mock<ILogger<FileService.Services.FileService>> _mockLogger;
    private readonly Mock<IMinioStorageService> _mockMinioService;
    private readonly FileService.Services.FileService _fileService;

    public FileServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<FileDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new FileDbContext(options);

        // Setup mocks
        _mockLogger = new Mock<ILogger<FileService.Services.FileService>>();
        _mockMinioService = new Mock<IMinioStorageService>();

        // Create service instance
        _fileService = new FileService.Services.FileService(
            _context,
            _mockLogger.Object,
            _mockMinioService.Object
        );
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    #region UploadFileAsync Tests

    [Fact]
    public async Task UploadFileAsync_WithValidData_ReturnsFileMetadata()
    {
        // Arrange
        var userId = 1L;
        var fileName = "test.txt";
        var contentType = "text/plain";
        var content = "Hello, World!";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(x => x.FileName).Returns(fileName);
        mockFile.Setup(x => x.ContentType).Returns(contentType);
        mockFile.Setup(x => x.Length).Returns(stream.Length);
        mockFile.Setup(x => x.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<((Stream s, CancellationToken ct) => stream.CopyToAsync(s, ct));

        _mockMinioService
            .Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("test-bucket/test-uploaded-file.txt"));
        _mockMinioService
            .Setup(x => x.GetPresignedUrlAsync(It.IsAny<string>()))
            .ReturnsAsync("http://localhost:9000/test-bucket/test-uploaded-file.txt"));

        // Act
        var result = await _fileService.UploadFileAsync(mockFile.Object, userId, null, null);

        // Assert
        result.Should().NotBeNull();
        result!.FileName.Should().Be(fileName);
        result!.ContentType.Should().Be(contentType);
        result!.Size.Should().Be(content.Length);
        result!.UploadedBy.Should().Be(userId);
    }

    [Fact]
    public async Task UploadFileAsync_WithLargeFile_ReturnsFileMetadata()
    {
        // Arrange
        var userId = 1L;
        var fileName = "large-file.bin";
        var contentType = "application/octet-stream";
        var largeContent = new byte[1024 * 1024]; // 1 MB
        var stream = new MemoryStream(largeContent);

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(x => x.FileName).Returns(fileName);
        mockFile.Setup(x => x.ContentType).Returns(contentType);
        mockFile.Setup(x => x.Length).Returns(stream.Length);
        mockFile.Setup(x => x.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<((Stream s, CancellationToken ct) => stream.CopyToAsync(s, ct));

        _mockMinioService
            .Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("test-bucket/large-file.bin"));
        _mockMinioService
            .Setup(x => x.GetPresignedUrlAsync(It.IsAny<string>()))
            .ReturnsAsync("http://localhost:9000/test-bucket/large-file.bin"));

        // Act
        var result = await _fileService.UploadFileAsync(mockFile.Object, userId, null, null);

        // Assert
        result.Should().NotBeNull();
        result!.Size.Should().Be(largeContent.Length);
    }

    #endregion

    #region GetFileMetadataAsync Tests

    [Fact]
    public async Task GetFileMetadataAsync_WithExistingFile_ReturnsMetadata()
    {
        // Arrange
        var file = new FileMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "existing-file.txt",
            ContentType = "text/plain",
            Size = 100,
            UploadedBy = 1,
            StoragePath = "test-bucket/existing-file.txt",
            CreatedAt = DateTime.UtcNow
        };
        _context.Files.Add(file);
        await _context.SaveChangesAsync();

        // Act
        var result = await _fileService.GetFileMetadataAsync(file.Id);

        // Assert
        result.Should().NotBeNull();
        result!.FileName.Should().Be("existing-file.txt");
    }

    [Fact]
    public async Task GetFileMetadataAsync_WithNonExistentFile_ReturnsNull()
    {
        // Act
        var result = await _fileService.GetFileMetadataAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region DeleteFileAsync Tests

    [Fact]
    public async Task DeleteFileAsync_WithExistingFile_DeletesFile()
    {
        // Arrange
        var userId = 1L;
        var file = new FileMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "to-delete.txt",
            ContentType = "text/plain",
            Size = 50,
            UploadedBy = userId,
            StoragePath = "test-bucket/to-delete.txt",
            CreatedAt = DateTime.UtcNow
        };
        _context.Files.Add(file);
        await _context.SaveChangesAsync();

        _mockMinioService
            .Setup(x => x.DeleteFileAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        // Act
        var result = await _fileService.DeleteFileAsync(file.Id, userId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteFileAsync_WithNonExistentFile_ReturnsFalse()
    {
        // Act
        var result = await _fileService.DeleteFileAsync(Guid.NewGuid(), 1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteFileAsync_WithWrongUser_ReturnsFalse()
    {
        // Arrange
        var file = new FileMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "other-user-file.txt",
            ContentType = "text/plain",
            Size = 50,
            UploadedBy = 2,
            StoragePath = "test-bucket/other-user-file.txt",
            CreatedAt = DateTime.UtcNow
        };
        _context.Files.Add(file);
        await _context.SaveChangesAsync();

        // Act
        var result = await _fileService.DeleteFileAsync(file.Id, 1);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetUserFilesAsync Tests

    [Fact]
    public async Task GetUserFilesAsync_ReturnsUserFiles()
    {
        // Arrange
        var userId = 1L;
        var file1 = new FileMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "file1.txt",
            ContentType = "text/plain",
            Size = 100,
            UploadedBy = userId,
            StoragePath = "test-bucket/file1.txt",
            CreatedAt = DateTime.UtcNow
        };
        var file2 = new FileMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "file2.txt",
            ContentType = "text/plain",
            Size = 200,
            UploadedBy = userId,
            StoragePath = "test-bucket/file2.txt",
            CreatedAt = DateTime.UtcNow
        };
        _context.Files.AddRange(file1, file2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _fileService.GetUserFilesAsync(userId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(f => f.FileName == "file1.txt");
        result.Should().Contain(f => f.FileName == "file2.txt");
    }

    [Fact]
    public async Task GetUserFilesAsync_WithNoFiles_ReturnsEmptyList()
    {
        // Arrange
        var userId = 999L;

        // Act
        var result = await _fileService.GetUserFilesAsync(userId);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion
}
