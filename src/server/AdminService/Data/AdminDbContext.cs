using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Data;

/// <summary>
/// Admin service database context
/// </summary>
public class AdminDbContext : DbContext
{
    public AdminDbContext(DbContextOptions<AdminDbContext> options) : base(options)
    {
    }
    
    public DbSet<DbUser> Users { get; set; }
    public DbSet<DbGroup> Groups { get; set; }
    public DbSet<DbSession> Sessions { get; set; }
    public DbSet<DbFile> Files { get; set; }
    public DbSet<DbMessage> Messages { get; set; }
    public DbSet<DbConference> Conferences { get; set; }
    public DbSet<DbAuditLog> AuditLog { get; set; }
    public DbSet<DbSystemSettings> SystemSettings { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // User entity
        modelBuilder.Entity<DbUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
        });
        
        // Group entity
        modelBuilder.Entity<DbGroup>(entity =>
        {
            entity.ToTable("groups");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.HasIndex(e => e.OwnerId);
            entity.HasIndex(e => e.CreatedAt);
        });
        
        // Session entity
        modelBuilder.Entity<DbSession>(entity =>
        {
            entity.ToTable("sessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.IsActive);
        });
        
        // File entity
        modelBuilder.Entity<DbFile>(entity =>
        {
            entity.ToTable("files");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.HasIndex(e => e.UploadedBy);
            entity.HasIndex(e => e.CreatedAt);
        });
        
        // Message entity
        modelBuilder.Entity<DbMessage>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.HasIndex(e => e.SenderId);
            entity.HasIndex(e => e.ChatId);
            entity.HasIndex(e => e.CreatedAt);
        });
        
        // Conference entity
        modelBuilder.Entity<DbConference>(entity =>
        {
            entity.ToTable("conferences");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.HasIndex(e => e.CreatedBy);
            entity.HasIndex(e => e.StartedAt);
        });
        
        // Audit log entity
        modelBuilder.Entity<DbAuditLog>(entity =>
        {
            entity.ToTable("audit_log");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.ActorUserId);
            entity.HasIndex(e => e.ResourceType);
        });
        
        // System settings entity
        modelBuilder.Entity<DbSystemSettings>(entity =>
        {
            entity.ToTable("system_settings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.HasIndex(e => e.Version);
        });
    }
}

#region Db Entities

/// <summary>
/// User database entity
/// </summary>
public class DbUser
{
    public long Id { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;
    
    [MaxLength(255)]
    public string? Email { get; set; }
    
    [MaxLength(20)]
    public string? PhoneNumber { get; set; }
    
    [MaxLength(100)]
    public string? FirstName { get; set; }
    
    [MaxLength(100)]
    public string? LastName { get; set; }
    
    [MaxLength(200)]
    public string? DisplayName { get; set; }
    
    public string? AvatarUrl { get; set; }
    
    public int Role { get; set; }
    
    public int Status { get; set; }
    
    public string? PasswordHash { get; set; }
    
    public DateTime? PasswordChangedAt { get; set; }
    
    public bool RequirePasswordChange { get; set; }
    
    public bool TwoFactorEnabled { get; set; }
    
    public string? TwoFactorSecret { get; set; }
    
    public string? LdapDn { get; set; }
    
    [MaxLength(100)]
    public string? Department { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime? UpdatedAt { get; set; }
    
    public DateTime? LastLoginAt { get; set; }
    
    public DateTime? DeletedAt { get; set; }
    
    public DateTime? SuspendedAt { get; set; }
    
    public string? SuspensionReason { get; set; }
}

/// <summary>
/// Group database entity
/// </summary>
public class DbGroup
{
    public long Id { get; set; }
    
    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;
    
    public string? Description { get; set; }
    
    [MaxLength(50)]
    public string? Username { get; set; }
    
    public string? AvatarUrl { get; set; }
    
    public int Type { get; set; }
    
    public long OwnerId { get; set; }
    
    public int MemberCount { get; set; }
    
    public bool IsVerified { get; set; }
    
    public bool IsRestricted { get; set; }
    
    public string? RestrictionReason { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime? UpdatedAt { get; set; }
    
    public DateTime? LastMessageAt { get; set; }
}

/// <summary>
/// Session database entity
/// </summary>
public class DbSession
{
    public long Id { get; set; }
    
    public long UserId { get; set; }
    
    [MaxLength(200)]
    public string DeviceName { get; set; } = string.Empty;
    
    [MaxLength(50)]
    public string DeviceType { get; set; } = string.Empty;
    
    [MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;
    
    [MaxLength(100)]
    public string? Location { get; set; }
    
    public string? UserAgent { get; set; }
    
    public string? RefreshToken { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime LastActiveAt { get; set; }
    
    public DateTime? ExpiresAt { get; set; }
    
    public DateTime? RevokedAt { get; set; }
    
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// File database entity
/// </summary>
public class DbFile
{
    public long Id { get; set; }
    
    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;
    
    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;
    
    public long SizeBytes { get; set; }
    
    public string? StoragePath { get; set; }
    
    public string? Hash { get; set; }
    
    public long UploadedBy { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public int? Width { get; set; }
    
    public int? Height { get; set; }
    
    public int? Duration { get; set; }
    
    public string? ThumbnailPath { get; set; }
}

/// <summary>
/// Message database entity
/// </summary>
public class DbMessage
{
    public long Id { get; set; }
    
    public long ChatId { get; set; }
    
    public long SenderId { get; set; }
    
    public string? Text { get; set; }
    
    public int MessageType { get; set; }
    
    public long? ReplyToId { get; set; }
    
    public long? ForwardFromId { get; set; }
    
    public long? FileId { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime? EditedAt { get; set; }
    
    public DateTime? DeletedAt { get; set; }
    
    public bool IsPinned { get; set; }
}

/// <summary>
/// Conference database entity
/// </summary>
public class DbConference
{
    public long Id { get; set; }
    
    [MaxLength(255)]
    public string? Title { get; set; }
    
    public long CreatedBy { get; set; }
    
    public int ConferenceType { get; set; }
    
    public DateTime StartedAt { get; set; }
    
    public DateTime? EndedAt { get; set; }
    
    public int ParticipantCount { get; set; }
    
    public int MaxParticipants { get; set; }
    
    public bool IsRecorded { get; set; }
    
    public string? RecordingPath { get; set; }
}

/// <summary>
/// Audit log database entity
/// </summary>
public class DbAuditLog
{
    public long Id { get; set; }
    
    public DateTime Timestamp { get; set; }
    
    public long? ActorUserId { get; set; }
    
    [MaxLength(100)]
    public string? ActorUsername { get; set; }
    
    [MaxLength(45)]
    public string? ActorIpAddress { get; set; }
    
    public int Action { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string ResourceType { get; set; } = string.Empty;
    
    public long? ResourceId { get; set; }
    
    [Required]
    public string Description { get; set; } = string.Empty;
    
    public string? OldValues { get; set; }
    
    public string? NewValues { get; set; }
    
    public bool Success { get; set; } = true;
    
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// System settings database entity
/// </summary>
public class DbSystemSettings
{
    public long Id { get; set; }
    
    public int Version { get; set; }
    
    [Required]
    public string SettingsJson { get; set; } = string.Empty;
    
    public DateTime UpdatedAt { get; set; }
    
    public long UpdatedBy { get; set; }
    
    public string? Comment { get; set; }
}

#endregion
