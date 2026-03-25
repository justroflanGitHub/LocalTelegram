using Microsoft.EntityFrameworkCore;
using ConferenceService.Models;

namespace ConferenceService.Data;

public class ConferenceDbContext : DbContext
{
    public ConferenceDbContext(DbContextOptions<ConferenceDbContext> options) : base(options)
    {
    }

    public DbSet<ConferenceRoom> ConferenceRooms => Set<ConferenceRoom>();
    public DbSet<ConferenceParticipant> ConferenceParticipants => Set<ConferenceParticipant>();
    public DbSet<CallSession> CallSessions => Set<CallSession>();
    public DbSet<IceServer> IceServers => Set<IceServer>();
    public DbSet<SignallingMessage> SignallingMessages => Set<SignallingMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ConferenceRoom configuration
        modelBuilder.Entity<ConferenceRoom>(entity =>
        {
            entity.ToTable("conference_rooms");
            
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnName("id");
            
            entity.Property(e => e.RoomCode)
                .HasMaxLength(50)
                .HasColumnName("room_code")
                .IsRequired();
            
            entity.HasIndex(e => e.RoomCode)
                .IsUnique();
            
            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .HasColumnName("title");
            
            entity.Property(e => e.ChatId)
                .HasColumnName("chat_id")
                .HasIndex();
            
            entity.Property(e => e.CreatorId)
                .HasColumnName("creator_id")
                .HasIndex();
            
            entity.Property(e => e.Type)
                .HasColumnName("type")
                .HasConversion<string>();
            
            entity.Property(e => e.MaxParticipants)
                .HasColumnName("max_participants");
            
            entity.Property(e => e.ParticipantCount)
                .HasColumnName("participant_count");
            
            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion<string>();
            
            entity.Property(e => e.HasPassword)
                .HasColumnName("has_password");
            
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(200)
                .HasColumnName("password_hash");
            
            entity.Property(e => e.VideoEnabled)
                .HasColumnName("video_enabled");
            
            entity.Property(e => e.AudioEnabled)
                .HasColumnName("audio_enabled");
            
            entity.Property(e => e.ScreenShareEnabled)
                .HasColumnName("screen_share_enabled");
            
            entity.Property(e => e.RecordingEnabled)
                .HasColumnName("recording_enabled");
            
            entity.Property(e => e.RecordingStartedAt)
                .HasColumnName("recording_started_at");
            
            entity.Property(e => e.StartTime)
                .HasColumnName("start_time");
            
            entity.Property(e => e.EndTime)
                .HasColumnName("end_time");
            
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at");
        });

        // ConferenceParticipant configuration
        modelBuilder.Entity<ConferenceParticipant>(entity =>
        {
            entity.ToTable("conference_participants");
            
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnName("id");
            
            entity.Property(e => e.RoomId)
                .HasColumnName("room_id")
                .HasIndex();
            
            entity.Property(e => e.UserId)
                .HasColumnName("user_id")
                .HasIndex();
            
            entity.Property(e => e.Role)
                .HasColumnName("role")
                .HasConversion<string>();
            
            entity.Property(e => e.VideoEnabled)
                .HasColumnName("video_enabled");
            
            entity.Property(e => e.AudioEnabled)
                .HasColumnName("audio_enabled");
            
            entity.Property(e => e.IsScreenSharing)
                .HasColumnName("is_screen_sharing");
            
            entity.Property(e => e.HandRaised)
                .HasColumnName("hand_raised");
            
            entity.Property(e => e.JoinedAt)
                .HasColumnName("joined_at");
            
            entity.Property(e => e.LeftAt)
                .HasColumnName("left_at");
            
            entity.Property(e => e.ConnectionId)
                .HasMaxLength(100)
                .HasColumnName("connection_id");
            
            entity.HasOne(e => e.Room)
                .WithMany(r => r.Participants)
                .HasForeignKey(e => e.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
            
            // Unique constraint for active participant
            entity.HasIndex(e => new { e.RoomId, e.UserId })
                .HasFilter("left_at IS NULL")
                .IsUnique();
        });

        // CallSession configuration
        modelBuilder.Entity<CallSession>(entity =>
        {
            entity.ToTable("call_sessions");
            
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnName("id");
            
            entity.Property(e => e.CallerId)
                .HasColumnName("caller_id")
                .HasIndex();
            
            entity.Property(e => e.CalleeId)
                .HasColumnName("callee_id")
                .HasIndex();
            
            entity.Property(e => e.ChatId)
                .HasColumnName("chat_id")
                .HasIndex();
            
            entity.Property(e => e.Type)
                .HasColumnName("type")
                .HasConversion<string>();
            
            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion<string>();
            
            entity.Property(e => e.StartedAt)
                .HasColumnName("started_at");
            
            entity.Property(e => e.EndedAt)
                .HasColumnName("ended_at");
            
            entity.Property(e => e.DurationSeconds)
                .HasColumnName("duration_seconds");
            
            entity.Property(e => e.EndReason)
                .HasColumnName("end_reason")
                .HasConversion<string>();
            
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at");
        });

        // IceServer configuration
        modelBuilder.Entity<IceServer>(entity =>
        {
            entity.ToTable("ice_servers");
            
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnName("id");
            
            entity.Property(e => e.Url)
                .HasMaxLength(500)
                .HasColumnName("url")
                .IsRequired();
            
            entity.Property(e => e.Type)
                .HasColumnName("type")
                .HasConversion<string>();
            
            entity.Property(e => e.Username)
                .HasMaxLength(100)
                .HasColumnName("username");
            
            entity.Property(e => e.Credential)
                .HasMaxLength(200)
                .HasColumnName("credential");
            
            entity.Property(e => e.IsActive)
                .HasColumnName("is_active");
            
            entity.Property(e => e.Priority)
                .HasColumnName("priority");
        });

        // SignallingMessage configuration
        modelBuilder.Entity<SignallingMessage>(entity =>
        {
            entity.ToTable("signalling_messages");
            
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnName("id");
            
            entity.Property(e => e.RoomId)
                .HasColumnName("room_id")
                .HasIndex();
            
            entity.Property(e => e.FromUserId)
                .HasColumnName("from_user_id");
            
            entity.Property(e => e.ToUserId)
                .HasColumnName("to_user_id");
            
            entity.Property(e => e.Type)
                .HasColumnName("type")
                .HasConversion<string>();
            
            entity.Property(e => e.Payload)
                .HasColumnName("payload")
                .HasColumnType("text");
            
            entity.Property(e => e.Timestamp)
                .HasColumnName("timestamp");
        });
    }
}
