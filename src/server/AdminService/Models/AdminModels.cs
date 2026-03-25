using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AdminService.Models;

#region User Management

/// <summary>
/// Admin user view
/// </summary>
public class AdminUser
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public UserRole Role { get; set; }
    public UserStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int SessionCount { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public string? LdapDn { get; set; }
    public string? Department { get; set; }
}

/// <summary>
/// User roles
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserRole
{
    User = 0,
    Moderator = 1,
    Admin = 2,
    SuperAdmin = 3
}

/// <summary>
/// User status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserStatus
{
    Active = 0,
    Suspended = 1,
    Deleted = 2,
    PendingVerification = 3
}

/// <summary>
/// User list request with pagination
/// </summary>
public class UserListRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? Search { get; set; }
    public UserRole? Role { get; set; }
    public UserStatus? Status { get; set; }
    public string? Department { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
    public string SortBy { get; set; } = "createdAt";
    public bool SortDescending { get; set; } = true;
}

/// <summary>
/// Paged user list result
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

/// <summary>
/// Update user request
/// </summary>
public class UpdateUserRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public UserRole? Role { get; set; }
    public UserStatus? Status { get; set; }
    public string? Department { get; set; }
}

/// <summary>
/// Reset password request
/// </summary>
public class ResetPasswordRequest
{
    [Required]
    [MinLength(8)]
    public string NewPassword { get; set; } = string.Empty;
    
    public bool RequireChangeOnNextLogin { get; set; } = true;
}

/// <summary>
/// Create user request
/// </summary>
public class CreateUserRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;
    
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    
    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;
    
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public string? Department { get; set; }
}

#endregion

#region Group Management

/// <summary>
/// Admin group view
/// </summary>
public class AdminGroup
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Username { get; set; }
    public GroupType Type { get; set; }
    public long OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsVerified { get; set; }
    public bool IsRestricted { get; set; }
}

/// <summary>
/// Group type
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GroupType
{
    Basic = 0,
    Supergroup = 1,
    Channel = 2
}

/// <summary>
/// Group list request
/// </summary>
public class GroupListRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? Search { get; set; }
    public GroupType? Type { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
    public string SortBy { get; set; } = "createdAt";
    public bool SortDescending { get; set; } = true;
}

#endregion

#region System Statistics

/// <summary>
/// System statistics dashboard
/// </summary>
public class SystemStatistics
{
    public UserStatistics Users { get; set; } = new();
    public MessageStatistics Messages { get; set; } = new();
    public GroupStatistics Groups { get; set; } = new();
    public StorageStatistics Storage { get; set; } = new();
    public ServerStatistics Server { get; set; } = new();
    public ConferenceStatistics Conferences { get; set; } = new();
}

/// <summary>
/// User statistics
/// </summary>
public class UserStatistics
{
    public long TotalUsers { get; set; }
    public long ActiveUsersToday { get; set; }
    public long ActiveUsersWeek { get; set; }
    public long ActiveUsersMonth { get; set; }
    public long NewUsersToday { get; set; }
    public long NewUsersWeek { get; set; }
    public long NewUsersMonth { get; set; }
    public long OnlineNow { get; set; }
    public Dictionary<string, int> UsersByDepartment { get; set; } = new();
    public List<TimeSeriesPoint> RegistrationTrend { get; set; } = new();
}

/// <summary>
/// Message statistics
/// </summary>
public class MessageStatistics
{
    public long TotalMessages { get; set; }
    public long MessagesToday { get; set; }
    public long MessagesWeek { get; set; }
    public long MessagesMonth { get; set; }
    public long MediaMessagesToday { get; set; }
    public double AverageMessagesPerUser { get; set; }
    public Dictionary<string, long> MessagesByType { get; set; } = new();
    public List<TimeSeriesPoint> MessageTrend { get; set; } = new();
}

/// <summary>
/// Group statistics
/// </summary>
public class GroupStatistics
{
    public long TotalGroups { get; set; }
    public long ActiveGroupsWeek { get; set; }
    public long ActiveGroupsMonth { get; set; }
    public long NewGroupsToday { get; set; }
    public double AverageMembersPerGroup { get; set; }
    public Dictionary<string, int> GroupsByType { get; set; } = new();
}

/// <summary>
/// Storage statistics
/// </summary>
public class StorageStatistics
{
    public long TotalFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public string TotalSizeFormatted => FormatSize(TotalSizeBytes);
    public long FilesUploadedToday { get; set; }
    public long SizeUploadedTodayBytes { get; set; }
    public string SizeUploadedTodayFormatted => FormatSize(SizeUploadedTodayBytes);
    public Dictionary<string, long> FilesByType { get; set; } = new();
    public Dictionary<string, long> SizeByType { get; set; } = new();
    
    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Server statistics
/// </summary>
public class ServerStatistics
{
    public double CpuUsagePercent { get; set; }
    public double MemoryUsagePercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryTotalBytes { get; set; }
    public double DiskUsagePercent { get; set; }
    public long DiskUsedBytes { get; set; }
    public long DiskTotalBytes { get; set; }
    public int ActiveConnections { get; set; }
    public int ActiveWebSockets { get; set; }
    public TimeSpan Uptime { get; set; }
    public DateTime StartTime { get; set; }
    public string Version { get; set; } = "1.0.0";
}

/// <summary>
/// Conference statistics
/// </summary>
public class ConferenceStatistics
{
    public int ActiveConferences { get; set; }
    public int TotalParticipants { get; set; }
    public long TotalConferencesToday { get; set; }
    public long TotalMinutesToday { get; set; }
    public double AverageParticipantsPerConference { get; set; }
    public double AverageDurationMinutes { get; set; }
}

/// <summary>
/// Time series data point
/// </summary>
public class TimeSeriesPoint
{
    public DateTime Timestamp { get; set; }
    public long Value { get; set; }
}

#endregion

#region Audit Log

/// <summary>
/// Audit log entry
/// </summary>
public class AuditLogEntry
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public long? ActorUserId { get; set; }
    public string? ActorUsername { get; set; }
    public string? ActorIpAddress { get; set; }
    public AuditAction Action { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public long? ResourceId { get; set; }
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object>? OldValues { get; set; }
    public Dictionary<string, object>? NewValues { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Audit action types
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuditAction
{
    // User actions
    UserCreated,
    UserUpdated,
    UserDeleted,
    UserSuspended,
    UserActivated,
    UserPasswordReset,
    UserRoleChanged,
    User2FAEnabled,
    User2FADisabled,
    
    // Group actions
    GroupCreated,
    GroupUpdated,
    GroupDeleted,
    GroupMemberAdded,
    GroupMemberRemoved,
    GroupRestricted,
    
    // System actions
    SystemConfigChanged,
    ServiceStarted,
    ServiceStopped,
    BackupCreated,
    BackupRestored,
    
    // Auth actions
    LoginSuccess,
    LoginFailed,
    Logout,
    SessionRevoked,
    
    // Admin actions
    AdminAction,
    ApiKeyCreated,
    ApiKeyRevoked
}

/// <summary>
/// Audit log request
/// </summary>
public class AuditLogRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public long? ActorUserId { get; set; }
    public AuditAction? Action { get; set; }
    public string? ResourceType { get; set; }
    public long? ResourceId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public bool? Success { get; set; }
    public string SortBy { get; set; } = "timestamp";
    public bool SortDescending { get; set; } = true;
}

#endregion

#region System Settings

/// <summary>
/// System settings
/// </summary>
public class SystemSettings
{
    public GeneralSettings General { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();
    public MessagingSettings Messaging { get; set; } = new();
    public FileSettings Files { get; set; } = new();
    public ConferenceSettings Conferences { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
    public LdapSettings Ldap { get; set; } = new();
}

/// <summary>
/// General settings
/// </summary>
public class GeneralSettings
{
    public string InstanceName { get; set; } = "LocalTelegram";
    public string? LogoUrl { get; set; }
    public int MaxUsers { get; set; } = 10000;
    public bool AllowRegistration { get; set; } = true;
    public bool RequireEmailVerification { get; set; } = true;
    public bool RequireAdminApproval { get; set; } = false;
    public string DefaultLanguage { get; set; } = "en";
    public string TimeZone { get; set; } = "UTC";
}

/// <summary>
/// Security settings
/// </summary>
public class SecuritySettings
{
    public int MinPasswordLength { get; set; } = 8;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireNumbers { get; set; } = true;
    public bool RequireSpecialChars { get; set; } = true;
    public int PasswordExpiryDays { get; set; } = 90;
    public int MaxLoginAttempts { get; set; } = 5;
    public int LockoutDurationMinutes { get; set; } = 30;
    public int SessionTimeoutMinutes { get; set; } = 1440; // 24 hours
    public bool TwoFactorRequired { get; set; } = false;
    public bool AllowRememberMe { get; set; } = true;
    public int RememberMeDays { get; set; } = 30;
}

/// <summary>
/// Messaging settings
/// </summary>
public class MessagingSettings
{
    public int MaxMessageLength { get; set; } = 4096;
    public bool AllowMessageEditing { get; set; } = true;
    public int MessageEditTimeLimitMinutes { get; set; } = 48 * 60; // 48 hours
    public bool AllowMessageDeletion { get; set; } = true;
    public bool AllowReactions { get; set; } = true;
    public int MaxReactionsPerMessage { get; set; } = 100;
    public bool AllowMessageForwarding { get; set; } = true;
    public int MessageHistoryLimit { get; set; } = 100000;
}

/// <summary>
/// File settings
/// </summary>
public class FileSettings
{
    public long MaxFileSizeBytes { get; set; } = 2L * 1024 * 1024 * 1024; // 2GB
    public long MaxStoragePerUserBytes { get; set; } = 10L * 1024 * 1024 * 1024; // 10GB
    public bool AllowChunkedUpload { get; set; } = true;
    public int ChunkSizeBytes { get; set; } = 512 * 1024; // 512KB
    public bool AutoCompressImages { get; set; } = true;
    public int ImageMaxWidth { get; set; } = 1280;
    public int ImageMaxHeight { get; set; } = 1280;
    public int ImageQuality { get; set; } = 85;
    public bool StripExifData { get; set; } = true;
    public List<string> AllowedFileTypes { get; set; } = new();
    public List<string> BlockedFileTypes { get; set; } = new() { "exe", "bat", "cmd", "sh" };
}

/// <summary>
/// Conference settings
/// </summary>
public class ConferenceSettings
{
    public int MaxParticipantsPerCall { get; set; } = 50;
    public int MaxVideoParticipants { get; set; } = 25;
    public int MaxCallDurationMinutes { get; set; } = 0; // 0 = unlimited
    public bool AllowScreenShare { get; set; } = true;
    public bool AllowRecording { get; set; } = true;
    public string DefaultVideoQuality { get; set; } = "720p";
    public bool EnableNoiseSuppression { get; set; } = true;
    public bool EnableVirtualBackgrounds { get; set; } = true;
}

/// <summary>
/// Notification settings
/// </summary>
public class NotificationSettings
{
    public bool PushNotificationsEnabled { get; set; } = true;
    public bool EmailNotificationsEnabled { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    public int BadgeCountMax { get; set; } = 99;
    public int NotificationRetentionDays { get; set; } = 30;
}

/// <summary>
/// LDAP settings
/// </summary>
public class LdapSettings
{
    public bool Enabled { get; set; } = false;
    public string? Server { get; set; }
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; } = true;
    public string? BaseDn { get; set; }
    public string? BindDn { get; set; }
    public string? BindPassword { get; set; }
    public string? UserSearchFilter { get; set; } = "(uid={0})";
    public string? GroupSearchFilter { get; set; } = "(member={0})";
    public bool SyncUsers { get; set; } = true;
    public int SyncIntervalMinutes { get; set; } = 60;
    public string? UsernameAttribute { get; set; } = "uid";
    public string? EmailAttribute { get; set; } = "mail";
    public string? DisplayNameAttribute { get; set; } = "cn";
    public string? DepartmentAttribute { get; set; } = "department";
}

#endregion

#region Health Check

/// <summary>
/// System health status
/// </summary>
public class HealthStatus
{
    public string Status { get; set; } = "healthy"; // healthy, degraded, unhealthy
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public List<ComponentHealth> Components { get; set; } = new();
}

/// <summary>
/// Component health status
/// </summary>
public class ComponentHealth
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "healthy";
    public string? Message { get; set; }
    public TimeSpan? ResponseTime { get; set; }
    public Dictionary<string, object>? Details { get; set; }
}

#endregion
