using Microsoft.EntityFrameworkCore;
using UserService.Models;

namespace UserService.Data;

public class UserDbContext : DbContext
{
    public UserDbContext(DbContextOptions<UserDbContext> options) : base(options)
    {
    }

    public DbSet<UserProfile> UserProfiles { get; set; }
    public DbSet<Contact> Contacts { get; set; }
    public DbSet<BlockedUser> BlockedUsers { get; set; }
    public DbSet<UserAvatar> UserAvatars { get; set; }
    public DbSet<PrivacySetting> PrivacySettings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // UserProfile configuration
        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Phone).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            
            entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.Bio).HasMaxLength(500);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Email).HasMaxLength(255);
        });

        // Contact configuration
        modelBuilder.Entity<Contact>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.ContactUserId }).IsUnique();
            
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.ContactName).HasMaxLength(100);
        });

        // BlockedUser configuration
        modelBuilder.Entity<BlockedUser>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.BlockedUserId }).IsUnique();
            
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // UserAvatar configuration
        modelBuilder.Entity<UserAvatar>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            
            entity.Property(e => e.FileId).IsRequired();
            entity.Property(e => e.SmallFileId).IsRequired();
        });

        // PrivacySetting configuration
        modelBuilder.Entity<PrivacySetting>(entity =>
        {
            entity.HasKey(e => e.UserId);
            
            entity.Property(e => e.LastSeenVisibility).HasDefaultValueString("everyone");
            entity.Property(e => e.PhoneVisibility).HasDefaultValueString("contacts");
            entity.Property(e => e.ProfilePhotoVisibility).HasDefaultValueString("everyone");
        });
    }
}
