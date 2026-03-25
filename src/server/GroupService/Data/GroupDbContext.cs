using Microsoft.EntityFrameworkCore;
using GroupService.Entities;

namespace GroupService.Data;

/// <summary>
/// Database context for GroupService
/// </summary>
public class GroupDbContext : DbContext
{
    public GroupDbContext(DbContextOptions<GroupDbContext> options) : base(options)
    {
    }
    
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<GroupInvite> GroupInvites => Set<GroupInvite>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Group configuration
        modelBuilder.Entity<Group>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Description).HasMaxLength(1024);
            entity.Property(e => e.AvatarUrl).HasMaxLength(512);
            entity.Property(e => e.InviteLink).HasMaxLength(64);
            
            entity.HasIndex(e => e.InviteLink).IsUnique();
            entity.HasIndex(e => e.OwnerId);
        });
        
        // GroupMember configuration
        modelBuilder.Entity<GroupMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.HasOne(e => e.Group)
                .WithMany(g => g.Members)
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasIndex(e => new { e.GroupId, e.UserId }).IsUnique();
            entity.HasIndex(e => e.UserId);
        });
        
        // GroupInvite configuration
        modelBuilder.Entity<GroupInvite>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.InviteCode).IsRequired().HasMaxLength(32);
            
            entity.HasOne(e => e.Group)
                .WithMany(g => g.Invites)
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasIndex(e => e.InviteCode).IsUnique();
        });
    }
}
