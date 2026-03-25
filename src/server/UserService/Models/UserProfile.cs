namespace UserService.Models;

public class UserProfile
{
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Bio { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Offline;
    public DateTime? LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public bool IsVerified { get; set; }
}

public enum UserStatus
{
    Offline = 0,
    Online = 1,
    Away = 2,
    Busy = 3
}

public class Contact
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long ContactUserId { get; set; }
    public string? ContactName { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsMutual { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    
    public UserProfile? User { get; set; }
}

public class BlockedUser
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long BlockedUserId { get; set; }
    public DateTime BlockedAt { get; set; } = DateTime.UtcNow;
    
    public UserProfile? User { get; set; }
}

public class UserAvatar
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string FileId { get; set; } = string.Empty;
    public string SmallFileId { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

public class PrivacySetting
{
    public long UserId { get; set; }
    public string LastSeenVisibility { get; set; } = "everyone"; // everyone, contacts, nobody
    public string PhoneVisibility { get; set; } = "contacts"; // everyone, contacts, nobody
    public string ProfilePhotoVisibility { get; set; } = "everyone"; // everyone, contacts, nobody
    public string BioVisibility { get; set; } = "everyone"; // everyone, contacts, nobody
    public bool AllowGroupInvites { get; set; } = true;
    public bool AllowVoiceCalls { get; set; } = true;
}

public class UserProfileDto
{
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public string? SmallAvatarUrl { get; set; }
    public UserStatus Status { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public bool IsVerified { get; set; }
    public bool IsContact { get; set; }
    public bool IsBlocked { get; set; }

    public static UserProfileDto FromProfile(UserProfile profile, string? avatarUrl = null, string? smallAvatarUrl = null, bool isContact = false, bool isBlocked = false)
    {
        return new UserProfileDto
        {
            UserId = profile.UserId,
            Username = profile.Username,
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Bio = profile.Bio,
            AvatarUrl = avatarUrl,
            SmallAvatarUrl = smallAvatarUrl,
            Status = profile.Status,
            LastSeenAt = profile.LastSeenAt,
            IsVerified = profile.IsVerified,
            IsContact = isContact,
            IsBlocked = isBlocked
        };
    }
}

public class UpdateProfileRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Bio { get; set; }
}

public class UpdatePrivacyRequest
{
    public string? LastSeenVisibility { get; set; }
    public string? PhoneVisibility { get; set; }
    public string? ProfilePhotoVisibility { get; set; }
    public string? BioVisibility { get; set; }
    public bool? AllowGroupInvites { get; set; }
    public bool? AllowVoiceCalls { get; set; }
}
