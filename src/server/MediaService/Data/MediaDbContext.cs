using Microsoft.EntityFrameworkCore;
using MediaService.Models;

namespace MediaService.Data;

public class MediaDbContext : DbContext
{
    public MediaDbContext(DbContextOptions<MediaDbContext> options) : base(options)
    {
    }

    public DbSet<VideoFile> VideoFiles => Set<VideoFile>();
    public DbSet<VideoVariant> VideoVariants => Set<VideoVariant>();
    public DbSet<VideoMessage> VideoMessages => Set<VideoMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // VideoFile configuration
        modelBuilder.Entity<VideoFile>(entity =>
        {
            entity.ToTable("video_files");
            
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnName("id");
            
            entity.Property(e => e.OriginalFileId)
                .HasColumnName("original_file_id")
                .HasIndex();
            
            entity.Property(e => e.DurationSeconds)
                .HasColumnName("duration_seconds");
            
            entity.Property(e => e.Width)
                .HasColumnName("width");
            
            entity.Property(e => e.Height)
                .HasColumnName("height");
            
            entity.Property(e => e.Codec)
                .HasMaxLength(20)
                .HasColumnName("codec");
            
            entity.Property(e => e.BitrateKbps)
                .HasColumnName("bitrate_kbps");
            
            entity.Property(e => e.FrameRate)
                .HasColumnName("frame_rate");
            
            entity.Property(e => e.FileSizeBytes)
                .HasColumnName("file_size_bytes");
            
            entity.Property(e => e.MimeType)
                .HasMaxLength(50)
                .HasColumnName("mime_type");
            
            entity.Property(e => e.HasAudio)
                .HasColumnName("has_audio");
            
            entity.Property(e => e.AudioCodec)
                .HasMaxLength(20)
                .HasColumnName("audio_codec");
            
            entity.Property(e => e.ThumbnailFileId)
                .HasColumnName("thumbnail_file_id");
            
            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion<string>();
            
            entity.Property(e => e.ErrorMessage)
                .HasColumnName("error_message");
            
            entity.Property(e => e.UserId)
                .HasColumnName("user_id")
                .HasIndex();
            
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at");
            
            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at");
        });

        // VideoVariant configuration
        modelBuilder.Entity<VideoVariant>(entity =>
        {
            entity.ToTable("video_variants");
            
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnName("id");
            
            entity.Property(e => e.VideoFileId)
                .HasColumnName("video_file_id")
                .HasIndex();
            
            entity.Property(e => e.Quality)
                .HasColumnName("quality")
                .HasConversion<string>();
            
            entity.Property(e => e.Width)
                .HasColumnName("width");
            
            entity.Property(e => e.Height)
                .HasColumnName("height");
            
            entity.Property(e => e.BitrateKbps)
                .HasColumnName("bitrate_kbps");
            
            entity.Property(e => e.StoragePath)
                .HasMaxLength(500)
                .HasColumnName("storage_path");
            
            entity.Property(e => e.FileSizeBytes)
                .HasColumnName("file_size_bytes");
            
            entity.HasOne(e => e.VideoFile)
                .WithMany()
                .HasForeignKey(e => e.VideoFileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // VideoMessage configuration
        modelBuilder.Entity<VideoMessage>(entity =>
        {
            entity.ToTable("video_messages");
            
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnName("id");
            
            entity.Property(e => e.FileId)
                .HasColumnName("file_id")
                .HasIndex();
            
            entity.Property(e => e.DurationSeconds)
                .HasColumnName("duration_seconds");
            
            entity.Property(e => e.Width)
                .HasColumnName("width");
            
            entity.Property(e => e.Height)
                .HasColumnName("height");
            
            entity.Property(e => e.AnimatedThumbnailId)
                .HasColumnName("animated_thumbnail_id");
            
            entity.Property(e => e.UserId)
                .HasColumnName("user_id")
                .HasIndex();
            
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at");
        });
    }
}
