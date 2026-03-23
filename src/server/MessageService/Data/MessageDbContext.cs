using Microsoft.EntityFrameworkCore;
using MessageService.Models;

namespace MessageService.Data;

public class MessageDbContext : DbContext
{
    public MessageDbContext(DbContextOptions<MessageDbContext> options) : base(options)
    {
    }

    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<ChatMember> ChatMembers => Set<ChatMember>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageStatusLog> MessageStatusLogs => Set<MessageStatusLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Chat entity
        modelBuilder.Entity<Chat>(entity =>
        {
            entity.ToTable("chats");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();
            entity.Property(e => e.Type).HasColumnName("type").HasConversion<string>().HasDefaultValue(ChatType.Private);
            entity.Property(e => e.Title).HasColumnName("title").HasMaxLength(255);
            entity.Property(e => e.Description).HasColumnName("description").HasColumnType("text");
            entity.Property(e => e.AvatarId).HasColumnName("avatar_id");
            entity.Property(e => e.OwnerId).HasColumnName("owner_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Settings).HasColumnName("settings").HasColumnType("jsonb").HasDefaultValue("{}");

            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.OwnerId);
        });

        // ChatMember entity
        modelBuilder.Entity<ChatMember>(entity =>
        {
            entity.ToTable("chat_members");
            entity.HasKey(e => new { e.ChatId, e.UserId });

            entity.Property(e => e.ChatId).HasColumnName("chat_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Role).HasColumnName("role").HasConversion<string>().HasDefaultValue(MemberRole.Member);
            entity.Property(e => e.JoinedAt).HasColumnName("joined_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.LastReadMessageId).HasColumnName("last_read_message_id");
            entity.Property(e => e.MutedUntil).HasColumnName("muted_until");

            entity.HasIndex(e => e.UserId);
        });

        // Message entity
        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();
            entity.Property(e => e.ChatId).HasColumnName("chat_id").IsRequired();
            entity.Property(e => e.SenderId).HasColumnName("sender_id").IsRequired();
            entity.Property(e => e.ReplyToId).HasColumnName("reply_to_id");
            entity.Property(e => e.ForwardFromId).HasColumnName("forward_from_id");
            entity.Property(e => e.Content).HasColumnName("content").HasColumnType("text");
            entity.Property(e => e.ContentType).HasColumnName("content_type").HasMaxLength(50).HasDefaultValue("text");
            entity.Property(e => e.MediaId).HasColumnName("media_id");
            entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasDefaultValue(MessageStatus.Sending);
            entity.Property(e => e.IsEdited).HasColumnName("is_edited").HasDefaultValue(false);
            entity.Property(e => e.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasDefaultValue("{}");

            entity.HasIndex(e => e.ChatId);
            entity.HasIndex(e => e.SenderId);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.ChatId, e.CreatedAt });

            entity.HasOne<Message>()
                .WithMany()
                .HasForeignKey(e => e.ReplyToId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<Message>()
                .WithMany()
                .HasForeignKey(e => e.ForwardFromId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // MessageStatusLog entity
        modelBuilder.Entity<MessageStatusLog>(entity =>
        {
            entity.ToTable("message_status_log");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();
            entity.Property(e => e.MessageId).HasColumnName("message_id").IsRequired();
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.MessageId);
            entity.HasIndex(e => e.UserId);

            entity.HasAlternateKey(e => new { e.MessageId, e.UserId });
        });
    }
}
