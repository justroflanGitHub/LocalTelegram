using System.IdentityModel.Tokens.Jwt;
using System.Text;
using AuthService.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace AuthService.Tests.Services;

public class TokenServiceTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly string _testSecretKey = "test-secret-key-for-unit-tests-min-32-chars";

    public TokenServiceTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        
        // Setup default configuration values
        _mockConfiguration.Setup(x => x["Jwt:Secret"]).Returns(_testSecretKey);
        _mockConfiguration.Setup(x => x["Jwt:Issuer"]).Returns("test-issuer");
        _mockConfiguration.Setup(x => x["Jwt:Audience"]).Returns("test-audience");
        _mockConfiguration.Setup(x => x["Jwt:ExpirationHours"]).Returns("1");
    }

    [Fact]
    public void Constructor_WithValidConfiguration_InitializesCorrectly()
    {
        // Act
        var service = new TokenService(_mockConfiguration.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullSecretKey_UsesDefaultKey()
    {
        // Arrange
        _mockConfiguration.Setup(x => x["Jwt:Secret"]).Returns((string?)null);
        _mockConfiguration.Setup(x => x["JWT_SECRET"]).Returns((string?)null);

        // Act
        var service = new TokenService(_mockConfiguration.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void GenerateAccessToken_WithValidUserId_ReturnsValidToken()
    {
        // Arrange
        var service = new TokenService(_mockConfiguration.Object);
        var userId = 12345L;

        // Act
        var token = service.GenerateAccessToken(userId);

        // Assert
        token.Should().NotBeNullOrEmpty();
        token.Split('.').Should().HaveCount(3); // JWT has 3 parts
    }

    [Fact]
    public void GenerateAccessToken_WithDifferentUserIds_GeneratesDifferentTokens()
    {
        // Arrange
        var service = new TokenService(_mockConfiguration.Object);
        var userId1 = 12345L;
        var userId2 = 67890L;

        // Act
        var token1 = service.GenerateAccessToken(userId1);
        var token2 = service.GenerateAccessToken(userId2);

        // Assert
        token1.Should().NotBe(token2);
    }

    [Fact]
    public void GenerateAccessToken_GeneratesTokenWithCorrectClaims()
    {
        // Arrange
        var service = new TokenService(_mockConfiguration.Object);
        var userId = 12345L;

        // Act
        var token = service.GenerateAccessToken(userId);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        jwtToken.Subject.Should().Be(userId.ToString());
        jwtToken.Issuer.Should().Be("test-issuer");
        jwtToken.Audiences.Should().Contain("test-audience");
        jwtToken.Claims.Should().Contain(c => c.Type == "type" && c.Value == "access");
    }

    [Fact]
    public void GenerateAccessToken_GeneratesUniqueJti()
    {
        // Arrange
        var service = new TokenService(_mockConfiguration.Object);
        var userId = 12345L;

        // Act
        var token1 = service.GenerateAccessToken(userId);
        var token2 = service.GenerateAccessToken(userId);

        var handler = new JwtSecurityTokenHandler();
        var jwt1 = handler.ReadJwtToken(token1);
        var jwt2 = handler.ReadJwtToken(token2);

        var jti1 = jwt1.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
        var jti2 = jwt2.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;

        // Assert
        jti1.Should().NotBe(jti2);
    }

    [Fact]
    public void ValidateToken_WithValidToken_ReturnsClaimsPrincipal()
    {
        // Arrange
        var service = new TokenService(_mockConfiguration.Object);
        var userId = 12345L;
        var token = service.GenerateAccessToken(userId);

        // Act
        var principal = service.ValidateToken(token);

        // Assert
        principal.Should().NotBeNull();
        principal!.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be(userId.ToString());
    }

    [Fact]
    public void ValidateToken_WithInvalidToken_ReturnsNull()
    {
        // Arrange
        var service = new TokenService(_mockConfiguration.Object);
        var invalidToken = "invalid.token.here";

        // Act
        var principal = service.ValidateToken(invalidToken);

        // Assert
        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WithEmptyToken_ReturnsNull()
    {
        // Arrange
        var service = new TokenService(_mockConfiguration.Object);

        // Act
        var principal = service.ValidateToken(string.Empty);

        // Assert
        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WithNullToken_ReturnsNull()
    {
        // Arrange
        var service = new TokenService(_mockConfiguration.Object);

        // Act
        var principal = service.ValidateToken(null!);

        // Assert
        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WithTokenFromDifferentSecret_ReturnsNull()
    {
        // Arrange
        var service1 = new TokenService(_mockConfiguration.Object);
        
        var mockConfig2 = new Mock<IConfiguration>();
        mockConfig2.Setup(x => x["Jwt:Secret"]).Returns("different-secret-key-for-testing-min-32-chars");
        mockConfig2.Setup(x => x["Jwt:Issuer"]).Returns("test-issuer");
        mockConfig2.Setup(x => x["Jwt:Audience"]).Returns("test-audience");
        var service2 = new TokenService(mockConfig2.Object);

        var token = service1.GenerateAccessToken(12345L);

        // Act
        var principal = service2.ValidateToken(token);

        // Assert
        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WithWrongIssuer_ReturnsNull()
    {
        // Arrange
        var service = new TokenService(_mockConfiguration.Object);
        var token = service.GenerateAccessToken(12345L);

        var mockConfig2 = new Mock<IConfiguration>();
        mockConfig2.Setup(x => x["Jwt:Secret"]).Returns(_testSecretKey);
        mockConfig2.Setup(x => x["Jwt:Issuer"]).Returns("wrong-issuer");
        mockConfig2.Setup(x => x["Jwt:Audience"]).Returns("test-audience");
        var service2 = new TokenService(mockConfig2.Object);

        // Act
        var principal = service2.ValidateToken(token);

        // Assert
        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WithWrongAudience_ReturnsNull()
    {
        // Arrange
        var service = new TokenService(_mockConfiguration.Object);
        var token = service.GenerateAccessToken(12345L);

        var mockConfig2 = new Mock<IConfiguration>();
        mockConfig2.Setup(x => x["Jwt:Secret"]).Returns(_testSecretKey);
        mockConfig2.Setup(x => x["Jwt:Issuer"]).Returns("test-issuer");
        mockConfig2.Setup(x => x["Jwt:Audience"]).Returns("wrong-audience");
        var service2 = new TokenService(mockConfig2.Object);

        // Act
        var principal = service2.ValidateToken(token);

        // Assert
        principal.Should().BeNull();
    }

    [Fact]
    public void GenerateAccessToken_WithZeroUserId_ReturnsValidToken()
    {
        // Arrange
        var service = new TokenService(_mockConfiguration.Object);
        var userId = 0L;

        // Act
        var token = service.GenerateAccessToken(userId);

        // Assert
        token.Should().NotBeNullOrEmpty();
        var principal = service.ValidateToken(token);
        principal!.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be("0");
    }

    [Fact]
    public void GenerateAccessToken_WithNegativeUserId_ReturnsValidToken()
    {
        // Arrange
        var service = new TokenService(_mockConfiguration.Object);
        var userId = -1L;

        // Act
        var token = service.GenerateAccessToken(userId);

        // Assert
        token.Should().NotBeNullOrEmpty();
        var principal = service.ValidateToken(token);
        principal!.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be("-1");
    }

    [Fact]
    public void GenerateAccessToken_WithLargeUserId_ReturnsValidToken()
    {
        // Arrange
        var service = new TokenService(_mockConfiguration.Object);
        var userId = long.MaxValue;

        // Act
        var token = service.GenerateAccessToken(userId);

        // Assert
        token.Should().NotBeNullOrEmpty();
        var principal = service.ValidateToken(token);
        principal!.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be(long.MaxValue.ToString());
    }
}
