using Microsoft.EntityFrameworkCore;
using FileService.Models;

namespace FileService.Data;

public class FileDbContext : DbContext
{
    public FileDbContext(DbContextOptions<FileDbContext> options) : base(options)
    {
    }

    public DbSet<FileMetadata> Files => Set<FileMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // FileMetadata entity
        modelBuilder.Entity<FileMetadata>(entity =>
        {
            entity.ToTable("files");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();
            entity.Property(e => e.UploaderId).HasColumnName("uploader_id").IsRequired();
            entity.Property(e => e.OriginalName).HasColumnName("original_name").HasMaxLength(255);
            entity.Property(e => e.MimeType).HasColumnName("mime_type").HasMaxLength(100);
            entity.Property(e => e.SizeBytes).HasColumnName("size_bytes").IsRequired();
            entity.Property(e => e.StoragePath).HasColumnName("storage_path").HasMaxLength(500).IsRequired();
            entity.Property(e => e.StorageProvider).HasColumnName("storage_provider").HasMaxLength(50).HasDefaultValue("minio");
            entity.Property(e => e.Checksum).HasColumnName("checksum").HasMaxLength(64);
            entity.Property(e => e.ThumbnailPath).HasColumnName("thumbnail_path").HasMaxLength(500);
            entity.Property(e => e.IsUploaded).HasColumnName("is_uploaded").HasDefaultValue(true);
            entity.Property(e => e.UploadCompletedAt).HasColumnName("upload_completed_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasDefaultValue("{}");

            entity.HasIndex(e => e.UploaderId);
            entity.HasIndex(e => e.Checksum);
        });
    }
}
