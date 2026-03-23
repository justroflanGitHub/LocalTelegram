# LocalTelegram Testing Guide

This document describes how to run and write tests for the LocalTelegram project.

## Prerequisites

### Install .NET SDK

1. Download .NET8 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0
2. Run the installer and follow the prompts
3. Verify installation:
   ```bash
   dotnet --version
   ```

## Running Tests

### Windows (PowerShell)

```powershell
# Run all tests
.\tests\run-tests.ps1

# Run with coverage
.\tests\run-tests.ps1 -Coverage

# Run specific test filter
.\tests\run-tests.ps1 -Filter "FullyQualifiedName~TokenServiceTests"

# Verbose output
.\tests\run-tests.ps1 -Verbose
```

### Linux/Mac (Bash)

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific project
dotnet test tests/AuthService.Tests

# Run specific test
dotnet test --filter "FullyQualifiedName~TokenServiceTests"
```

## Test Structure

```
tests/
├── Directory.Build.props          # Common test project settings
├── run-tests.ps1                  # Windows test runner script
├── AuthService.Tests/
│   ├── AuthService.Tests.csproj
│   └── Services/
│       ├── TokenServiceTests.cs   # 15 tests
│       └── AuthServiceTests.cs    # 14 tests
├── MessageService.Tests/
│   ├── MessageService.Tests.csproj
│   └── Services/
│       └── ChatServiceTests.cs    # 20+ tests
└── FileService.Tests/
    ├── FileService.Tests.csproj
    └── Services/
        └── FileServiceTests.cs     # 10+ tests
```

## Test Coverage

### AuthService.Tests

| Class | Tests | Description |
|-------|-------|-------------|
| TokenServiceTests | 15 | JWT token generation and validation |
| AuthServiceTests | 14 | User registration, login, logout |

**TokenServiceTests:**
- `Constructor_WithValidConfiguration_InitializesCorrectly`
- `Constructor_WithNullSecretKey_UsesDefaultKey`
- `GenerateAccessToken_WithValidUserId_ReturnsValidToken`
- `GenerateAccessToken_WithDifferentUserIds_GeneratesDifferentTokens`
- `GenerateAccessToken_GeneratesTokenWithCorrectClaims`
- `GenerateAccessToken_GeneratesUniqueJti`
- `ValidateToken_WithValidToken_ReturnsClaimsPrincipal`
- `ValidateToken_WithInvalidToken_ReturnsNull`
- `ValidateToken_WithEmptyToken_ReturnsNull`
- `ValidateToken_WithNullToken_ReturnsNull`
- `ValidateToken_WithTokenFromDifferentSecret_ReturnsNull`
- `ValidateToken_WithWrongIssuer_ReturnsNull`
- `ValidateToken_WithWrongAudience_ReturnsNull`
- `GenerateAccessToken_WithZeroUserId_ReturnsValidToken`
- `GenerateAccessToken_WithNegativeUserId_ReturnsValidToken`
- `GenerateAccessToken_WithLargeUserId_ReturnsValidToken`

**AuthServiceTests:**
- `RegisterAsync_WithValidData_ReturnsSuccess`
- `RegisterAsync_WithExistingUsername_ReturnsError`
- `RegisterAsync_WithExistingEmail_ReturnsError`
- `RegisterAsync_WithExistingPhone_ReturnsError`
- `RegisterAsync_ConvertsUsernameToLowercase`
- `RegisterAsync_HashesPassword`
- `LoginAsync_WithValidCredentials_ReturnsSuccess`
- `LoginAsync_WithInvalidUsername_ReturnsError`
- `LoginAsync_WithInvalidPassword_ReturnsError`
- `LoginAsync_WithInactiveUser_ReturnsError`
- `LoginAsync_UpdatesUserStatusToOnline`
- `GetUserByIdAsync_WithExistingId_ReturnsUser`
- `GetUserByIdAsync_WithNonExistentId_ReturnsNull`
- `GetUserByUsernameAsync_WithExistingUsername_ReturnsUser`
- `GetUserByUsernameAsync_WithNonExistentUsername_ReturnsNull`
- `LogoutAsync_WithValidSession_RevokesSession`
- `LogoutAsync_WithNonExistentSession_ReturnsFalse`
- `LogoutAllAsync_WithMultipleSessions_RevokesAllSessions`

### MessageService.Tests

| Class | Tests | Description |
|-------|-------|-------------|
| ChatServiceTests | 20+ | Chat and member management |

**ChatServiceTests:**
- `CreateChatAsync_WithValidData_ReturnsChat`
- `CreateChatAsync_AddsOwnerAsMember`
- `CreateChatAsync_WithMemberIds_AddsAllMembers`
- `CreateChatAsync_WithPrivateType_CreatesPrivateChat`
- `GetChatAsync_WithMemberUser_ReturnsChat`
- `GetChatAsync_WithNonMemberUser_ReturnsNull`
- `GetChatAsync_WithNonExistentChat_ReturnsNull`
- `GetUserChatsAsync_ReturnsUserChats`
- `GetUserChatsAsync_WithNoChats_ReturnsEmptyList`
- `UpdateChatAsync_WithOwner_UpdatesChat`
- `UpdateChatAsync_WithAdmin_UpdatesChat`
- `UpdateChatAsync_WithRegularMember_ReturnsNull`
- `UpdateChatAsync_WithNonMember_ReturnsNull`
- `DeleteChatAsync_WithOwner_DeletesChat`
- `DeleteChatAsync_WithNonOwner_ReturnsFalse`
- `DeleteChatAsync_WithNonExistentChat_ReturnsFalse`
- `AddMemberAsync_WithAdmin_AddsMember`
- `AddMemberAsync_ToPrivateChat_ReturnsFalse`
- `AddMemberAsync_WithExistingMember_ReturnsTrue`
- `RemoveMemberAsync_WithAdmin_RemovesMember`
- `RemoveMemberAsync_SelfRemoval_ReturnsTrue`
- `RemoveMemberAsync_CannotRemoveOwner_ReturnsFalse`
- `GetOrCreatePrivateChatAsync_CreatesNewChat`
- `GetOrCreatePrivateChatAsync_ReturnsExistingChat`
- `GetOrCreatePrivateChatAsync_AddsBothUsersAsMembers`
- `UpdateMemberRoleAsync_WithOwner_UpdatesRole`
- `UpdateMemberRoleAsync_CannotSetOwner_ReturnsFalse`
- `UpdateMemberRoleAsync_NonOwnerCannotUpdate_ReturnsFalse`

### FileService.Tests

| Class | Tests | Description |
|-------|-------|-------------|
| FileServiceTests | 10+ | File upload and management |

**FileServiceTests:**
- `UploadFileAsync_WithValidData_ReturnsFileMetadata`
- `UploadFileAsync_WithLargeFile_ReturnsFileMetadata`
- `GetFileMetadataAsync_WithExistingFile_ReturnsMetadata`
- `GetFileMetadataAsync_WithNonExistentFile_ReturnsNull`
- `DeleteFileAsync_WithExistingFile_DeletesFile`
- `DeleteFileAsync_WithNonExistentFile_ReturnsFalse`
- `DeleteFileAsync_WithWrongUser_ReturnsFalse`
- `GetUserFilesAsync_ReturnsUserFiles`
- `GetUserFilesAsync_WithNoFiles_ReturnsEmptyList`

## Writing New Tests

### Test Class Template

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace YourService.Tests.Services;

public class YourServiceTests : IDisposable
{
    private readonly YourDbContext _context;
    private readonly Mock<ILogger<YourService>> _mockLogger;
    private readonly YourService _service;

    public YourServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<YourDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new YourDbContext(options);

        // Setup mocks
        _mockLogger = new Mock<ILogger<YourService>>();

        // Create service instance
        _service = new YourService(_context, _mockLogger.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task YourMethod_WithValidInput_ReturnsExpectedResult()
    {
        // Arrange
        var input = "test";

        // Act
        var result = await _service.YourMethodAsync(input);

        // Assert
        result.Should().NotBeNull();
    }
}
```

### Best Practices

1. **Use In-Memory Database**: Each test class should create a fresh database
2. **Dispose Resources**: Implement `IDisposable` to clean up after tests
3. **Use FluentAssertions**: More readable assertions
4. **Mock External Dependencies**: Use Moq for external services
5. **Test Edge Cases**: Null values, empty strings, boundary conditions
6. **Test Error Conditions**: Invalid inputs, unauthorized access
7. **One Assert Per Test**: Keep tests focused and readable

## Continuous Integration

Tests are automatically run in CI/CD pipelines. Example GitHub Actions workflow:

```yaml
name: Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: 8.0.x
      - name: Run tests
        run: dotnet test --collect:"XPlat Code Coverage"
      - name: Upload coverage
        uses: codecov/codecov-action@v3
```

## Troubleshooting

### Tests Fail with Database Errors

Ensure the in-memory database is properly configured:
```csharp
.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
```

### Tests Fail with Null Reference

Check that all dependencies are properly mocked:
```csharp
_mockDependency.Setup(x => x.Method()).Returns(expectedValue);
```

### Tests Pass Locally but Fail in CI

- Check for environment-specific configurations
- Ensure all required packages are in .csproj
- Verify file paths use proper separators
