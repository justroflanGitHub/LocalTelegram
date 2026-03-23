using System.Text.Json.Serialization;

namespace AuthService.Models;

public enum UserStatus
{
    Online,
    Offline,
    Away,
    Busy
}

public class User
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Bio { get; set; }
    public long? AvatarId { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Offline;
    public DateTime? LastSeenAt { get; set; }
    public bool IsVerified { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Settings { get; set; } = new();
}

public class Session
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string? RefreshTokenHash { get; set; }
    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RefreshExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
}

public class UserDevice
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string? DeviceToken { get; set; }
    public string DeviceType { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string? PushToken { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// DTOs
public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
    public string? DeviceType { get; set; }
}

public class LoginResponse
{
    public bool Success { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public UserDto? User { get; set; }
    public string? Error { get; set; }
}

public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class UserDto
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Bio { get; set; }
    public long? AvatarId { get; set; }
    public UserStatus Status { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public bool IsVerified { get; set; }
    public DateTime CreatedAt { get; set; }

    public static UserDto FromUser(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Phone = user.Phone,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Bio = user.Bio,
            AvatarId = user.AvatarId,
            Status = user.Status,
            LastSeenAt = user.LastSeenAt,
            IsVerified = user.IsVerified,
            CreatedAt = user.CreatedAt
        };
    }
}

public class UpdateUserRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Bio { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
