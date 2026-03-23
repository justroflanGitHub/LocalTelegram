using Microsoft.EntityFrameworkCore;
using AuthService.Models;

namespace AuthService.Data;

public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();
            entity.Property(e => e.Username).HasColumnName("username").HasMaxLength(50).IsRequired();
            entity.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(20);
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(255);
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
            entity.Property(e => e.FirstName).HasColumnName("first_name").HasMaxLength(100);
            entity.Property(e => e.LastName).HasColumnName("last_name").HasMaxLength(100);
            entity.Property(e => e.Bio).HasColumnName("bio").HasColumnType("text");
            entity.Property(e => e.AvatarId).HasColumnName("avatar_id");
            entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasDefaultValue(UserStatus.Offline);
            entity.Property(e => e.LastSeenAt).HasColumnName("last_seen_at");
            entity.Property(e => e.IsVerified).HasColumnName("is_verified").HasDefaultValue(false);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Settings).HasColumnName("settings").HasColumnType("jsonb").HasDefaultValue("{}");

            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Phone).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // Session entity
        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("sessions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.TokenHash).HasColumnName("token_hash").HasMaxLength(255).IsRequired();
            entity.Property(e => e.RefreshTokenHash).HasColumnName("refresh_token_hash").HasMaxLength(255);
            entity.Property(e => e.DeviceInfo).HasColumnName("device_info").HasMaxLength(255);
            entity.Property(e => e.IpAddress).HasColumnName("ip_address").HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasColumnName("user_agent").HasColumnType("text");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();
            entity.Property(e => e.RefreshExpiresAt).HasColumnName("refresh_expires_at");
            entity.Property(e => e.IsRevoked).HasColumnName("is_revoked").HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.LastAccessedAt).HasColumnName("last_accessed_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.TokenHash);
            entity.HasIndex(e => e.ExpiresAt);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // UserDevice entity
        modelBuilder.Entity<UserDevice>(entity =>
        {
            entity.ToTable("user_devices");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.DeviceToken).HasColumnName("device_token").HasMaxLength(500);
            entity.Property(e => e.DeviceType).HasColumnName("device_type").HasMaxLength(20).IsRequired();
            entity.Property(e => e.DeviceName).HasColumnName("device_name").HasMaxLength(100);
            entity.Property(e => e.PushToken).HasColumnName("push_token").HasMaxLength(500);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.PushToken);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
