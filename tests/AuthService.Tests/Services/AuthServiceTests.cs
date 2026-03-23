using AuthService.Data;
using AuthService.Models;
using AuthService.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AuthService.Tests.Services;

public class AuthServiceTests : IDisposable
{
    private readonly AuthDbContext _context;
    private readonly Mock<ITokenService> _mockTokenService;
    private readonly Mock<IRedisService> _mockRedisService;
    private readonly Mock<ILogger<AuthService.Services.AuthService>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly AuthService.Services.AuthService _authService;

    public AuthServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AuthDbContext(options);

        // Setup mocks
        _mockTokenService = new Mock<ITokenService>();
        _mockRedisService = new Mock<IRedisService>();
        _mockLogger = new Mock<ILogger<AuthService.Services.AuthService>>();
        _mockConfiguration = new Mock<IConfiguration>();

        // Create service instance
        _authService = new AuthService.Services.AuthService(
            _context,
            _mockTokenService.Object,
            _mockRedisService.Object,
            _mockLogger.Object,
            _mockConfiguration.Object
        );
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    #region RegisterAsync Tests

    [Fact]
    public async Task RegisterAsync_WithValidData_ReturnsSuccess()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Username = "testuser",
            Password = "TestPassword123!",
            FirstName = "Test",
            LastName = "User"
        };

        _mockTokenService.Setup(x => x.GenerateAccessToken(It.IsAny<long>()))
            .Returns("test-access-token");

        // Act
        var result = await _authService.RegisterAsync(request, "127.0.0.1", "TestAgent");

        // Assert
        result.Success.Should().BeTrue();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.User.Should().NotBeNull();
        result.User!.Username.Should().Be("testuser");
    }

    [Fact]
    public async Task RegisterAsync_WithExistingUsername_ReturnsError()
    {
        // Arrange
        var existingUser = new User
        {
            Username = "existinguser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Status = UserStatus.Offline
        };
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var request = new RegisterRequest
        {
            Username = "existinguser",
            Password = "TestPassword123!"
        };

        // Act
        var result = await _authService.RegisterAsync(request, "127.0.0.1", "TestAgent");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Username already exists");
    }

    [Fact]
    public async Task RegisterAsync_WithExistingEmail_ReturnsError()
    {
        // Arrange
        var existingUser = new User
        {
            Username = "user1",
            Email = "test@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Status = UserStatus.Offline
        };
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var request = new RegisterRequest
        {
            Username = "newuser",
            Password = "TestPassword123!",
            Email = "test@example.com"
        };

        // Act
        var result = await _authService.RegisterAsync(request, "127.0.0.1", "TestAgent");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Email already exists");
    }

    [Fact]
    public async Task RegisterAsync_WithExistingPhone_ReturnsError()
    {
        // Arrange
        var existingUser = new User
        {
            Username = "user1",
            Phone = "+1234567890",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Status = UserStatus.Offline
        };
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var request = new RegisterRequest
        {
            Username = "newuser",
            Password = "TestPassword123!",
            Phone = "+1234567890"
        };

        // Act
        var result = await _authService.RegisterAsync(request, "127.0.0.1", "TestAgent");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Phone already exists");
    }

    [Fact]
    public async Task RegisterAsync_ConvertsUsernameToLowercase()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Username = "TestUser",
            Password = "TestPassword123!"
        };

        _mockTokenService.Setup(x => x.GenerateAccessToken(It.IsAny<long>()))
            .Returns("test-access-token");

        // Act
        var result = await _authService.RegisterAsync(request, "127.0.0.1", "TestAgent");

        // Assert
        result.Success.Should().BeTrue();
        result.User!.Username.Should().Be("testuser");
    }

    [Fact]
    public async Task RegisterAsync_HashesPassword()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Username = "testuser",
            Password = "TestPassword123!"
        };

        _mockTokenService.Setup(x => x.GenerateAccessToken(It.IsAny<long>()))
            .Returns("test-access-token");

        // Act
        await _authService.RegisterAsync(request, "127.0.0.1", "TestAgent");

        // Assert
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == "testuser");
        user.Should().NotBeNull();
        user!.PasswordHash.Should().NotBe("TestPassword123!");
        BCrypt.Net.BCrypt.Verify("TestPassword123!", user.PasswordHash).Should().BeTrue();
    }

    #endregion

    #region LoginAsync Tests

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsSuccess()
    {
        // Arrange
        var password = "TestPassword123!";
        var user = new User
        {
            Username = "testuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Status = UserStatus.Offline,
            IsActive = true
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var request = new LoginRequest
        {
            Username = "testuser",
            Password = password
        };

        _mockTokenService.Setup(x => x.GenerateAccessToken(It.IsAny<long>()))
            .Returns("test-access-token");

        // Act
        var result = await _authService.LoginAsync(request, "127.0.0.1", "TestAgent");

        // Assert
        result.Success.Should().BeTrue();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.User.Should().NotBeNull();
    }

    [Fact]
    public async Task LoginAsync_WithInvalidUsername_ReturnsError()
    {
        // Arrange
        var request = new LoginRequest
        {
            Username = "nonexistent",
            Password = "password"
        };

        // Act
        var result = await _authService.LoginAsync(request, "127.0.0.1", "TestAgent");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Invalid credentials");
    }

    [Fact]
    public async Task LoginAsync_WithInvalidPassword_ReturnsError()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correctpassword"),
            Status = UserStatus.Offline,
            IsActive = true
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var request = new LoginRequest
        {
            Username = "testuser",
            Password = "wrongpassword"
        };

        // Act
        var result = await _authService.LoginAsync(request, "127.0.0.1", "TestAgent");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Invalid credentials");
    }

    [Fact]
    public async Task LoginAsync_WithInactiveUser_ReturnsError()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Status = UserStatus.Offline,
            IsActive = false
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var request = new LoginRequest
        {
            Username = "testuser",
            Password = "password"
        };

        // Act
        var result = await _authService.LoginAsync(request, "127.0.0.1", "TestAgent");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Account is disabled");
    }

    [Fact]
    public async Task LoginAsync_UpdatesUserStatusToOnline()
    {
        // Arrange
        var password = "TestPassword123!";
        var user = new User
        {
            Username = "testuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Status = UserStatus.Offline,
            IsActive = true
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var request = new LoginRequest
        {
            Username = "testuser",
            Password = password
        };

        _mockTokenService.Setup(x => x.GenerateAccessToken(It.IsAny<long>()))
            .Returns("test-access-token");

        // Act
        await _authService.LoginAsync(request, "127.0.0.1", "TestAgent");

        // Assert
        var updatedUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == "testuser");
        updatedUser.Should().NotBeNull();
        updatedUser!.Status.Should().Be(UserStatus.Online);
    }

    #endregion

    #region GetUserByIdAsync Tests

    [Fact]
    public async Task GetUserByIdAsync_WithExistingId_ReturnsUser()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            PasswordHash = "hash",
            Status = UserStatus.Offline
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Act
        var result = await _authService.GetUserByIdAsync(user.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Username.Should().Be("testuser");
    }

    [Fact]
    public async Task GetUserByIdAsync_WithNonExistentId_ReturnsNull()
    {
        // Act
        var result = await _authService.GetUserByIdAsync(999999);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetUserByUsernameAsync Tests

    [Fact]
    public async Task GetUserByUsernameAsync_WithExistingUsername_ReturnsUser()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            PasswordHash = "hash",
            Status = UserStatus.Offline
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Act
        var result = await _authService.GetUserByUsernameAsync("testuser");

        // Assert
        result.Should().NotBeNull();
        result!.Username.Should().Be("testuser");
    }

    [Fact]
    public async Task GetUserByUsernameAsync_WithNonExistentUsername_ReturnsNull()
    {
        // Act
        var result = await _authService.GetUserByUsernameAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region LogoutAsync Tests

    [Fact]
    public async Task LogoutAsync_WithValidSession_RevokesSession()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            PasswordHash = "hash",
            Status = UserStatus.Online
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var session = new Session
        {
            UserId = user.Id,
            RefreshTokenHash = "hash",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            RefreshExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        _context.Sessions.Add(session);
        await _context.SaveChangesAsync();

        // Act
        var result = await _authService.LogoutAsync(session.Id);

        // Assert
        result.Should().BeTrue();
        var revokedSession = await _context.Sessions.FindAsync(session.Id);
        revokedSession!.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task LogoutAsync_WithNonExistentSession_ReturnsFalse()
    {
        // Act
        var result = await _authService.LogoutAsync(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region LogoutAllAsync Tests

    [Fact]
    public async Task LogoutAllAsync_WithMultipleSessions_RevokesAllSessions()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            PasswordHash = "hash",
            Status = UserStatus.Online
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var sessions = new List<Session>
        {
            new() { UserId = user.Id, RefreshTokenHash = "hash1", ExpiresAt = DateTime.UtcNow.AddDays(1), RefreshExpiresAt = DateTime.UtcNow.AddDays(7) },
            new() { UserId = user.Id, RefreshTokenHash = "hash2", ExpiresAt = DateTime.UtcNow.AddDays(1), RefreshExpiresAt = DateTime.UtcNow.AddDays(7) },
            new() { UserId = user.Id, RefreshTokenHash = "hash3", ExpiresAt = DateTime.UtcNow.AddDays(1), RefreshExpiresAt = DateTime.UtcNow.AddDays(7) }
        };
        _context.Sessions.AddRange(sessions);
        await _context.SaveChangesAsync();

        // Act
        var result = await _authService.LogoutAllAsync(user.Id);

        // Assert
        result.Should().BeTrue();
        var userSessions = await _context.Sessions.Where(s => s.UserId == user.Id).ToListAsync();
        userSessions.Should().AllSatisfy(s => s.IsRevoked.Should().BeTrue());
    }

    #endregion
}
