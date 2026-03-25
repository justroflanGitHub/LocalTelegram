using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

namespace FileService.Services;

public interface IImageProcessingService
{
    Task<Stream> CompressImageAsync(Stream imageData, string mimeType, ImageCompressionOptions? options = null);
    Task<Stream> RemoveExifDataAsync(Stream imageData, string mimeType);
    Task<ImageMetadata> ExtractMetadataAsync(Stream imageData);
    Task<Stream> ResizeImageAsync(Stream imageData, string mimeType, int maxWidth, int maxHeight);
    Task<Stream> CreateThumbnailAsync(Stream imageData, string mimeType, int size);
    bool IsSupportedImage(string mimeType);
}

public class ImageCompressionOptions
{
    public int Quality { get; set; } = 85;
    public int MaxWidth { get; set; } = 2048;
    public int MaxHeight { get; set; } = 2048;
    public bool RemoveMetadata { get; set; } = true;
    public OutputFormat Format { get; set; } = OutputFormat.KeepOriginal;
}

public enum OutputFormat
{
    KeepOriginal,
    Jpeg,
    Png,
    WebP
}

public class ImageMetadata
{
    public int Width { get; set; }
    public int Height { get; set; }
    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }
    public DateTime? TakenAt { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Software { get; set; }
    public int? Orientation { get; set; }
    public int? IsoSpeed { get; set; }
    public double? FNumber { get; set; }
    public double? ExposureTime { get; set; }
    public double? FocalLength { get; set; }
}

public class ImageProcessingService : IImageProcessingService
{
    private readonly ILogger<ImageProcessingService> _logger;
    private readonly IConfiguration _configuration;
    
    private static readonly string[] SupportedMimeTypes = 
    {
        "image/jpeg", "image/png", "image/gif", "image/webp", "image/bmp"
    };

    public ImageProcessingService(
        ILogger<ImageProcessingService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public bool IsSupportedImage(string mimeType)
    {
        return SupportedMimeTypes.Contains(mimeType.ToLowerInvariant());
    }

    public async Task<Stream> CompressImageAsync(Stream imageData, string mimeType, ImageCompressionOptions? options = null)
    {
        options ??= new ImageCompressionOptions();

        try
        {
            imageData.Position = 0;
            using var image = await Image.LoadAsync(imageData);

            // Resize if needed
            if (image.Width > options.MaxWidth || image.Height > options.MaxHeight)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(options.MaxWidth, options.MaxHeight),
                    Mode = ResizeMode.Max
                }));
            }

            // Remove metadata if requested
            if (options.RemoveMetadata)
            {
                image.Metadata.ExifProfile = null;
                image.Metadata.IccProfile = null;
                image.Metadata.IptcProfile = null;
                image.Metadata.XmpProfile = null;
            }

            // Determine output format
            var outputStream = new MemoryStream();
            var outputMimeType = DetermineOutputMimeType(mimeType, options.Format);

            await SaveImageAsync(image, outputStream, outputMimeType, options.Quality);

            outputStream.Position = 0;
            return outputStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error compressing image");
            throw;
        }
    }

    public async Task<Stream> RemoveExifDataAsync(Stream imageData, string mimeType)
    {
        try
        {
            imageData.Position = 0;
            using var image = await Image.LoadAsync(imageData);

            // Remove all metadata profiles
            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;

            var outputStream = new MemoryStream();
            await SaveImageAsync(image, outputStream, mimeType, 95);

            outputStream.Position = 0;
            return outputStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing EXIF data");
            throw;
        }
    }

    public async Task<ImageMetadata> ExtractMetadataAsync(Stream imageData)
    {
        var metadata = new ImageMetadata();

        try
        {
            imageData.Position = 0;
            using var image = await Image.LoadAsync(imageData);

            metadata.Width = image.Width;
            metadata.Height = image.Height;

            var exif = image.Metadata.ExifProfile;
            if (exif != null)
            {
                metadata.CameraMake = GetExifValue(exif, ExifTag.Make);
                metadata.CameraModel = GetExifValue(exif, ExifTag.Model);
                metadata.Software = GetExifValue(exif, ExifTag.Software);

                var dateTimeOriginal = GetExifValue(exif, ExifTag.DateTimeOriginal);
                if (!string.IsNullOrEmpty(dateTimeOriginal))
                {
                    metadata.TakenAt = ParseExifDateTime(dateTimeOriginal);
                }

                // GPS coordinates
                var gpsLatitude = exif.GetValue(ExifTag.GPSLatitude);
                var gpsLongitude = exif.GetValue(ExifTag.GPSLongitude);
                var gpsLatitudeRef = exif.GetValue(ExifTag.GPSLatitudeRef);
                var gpsLongitudeRef = exif.GetValue(ExifTag.GPSLongitudeRef);

                if (gpsLatitude != null && gpsLongitude != null)
                {
                    metadata.Latitude = ConvertGpsCoordinate(
                        gpsLatitude.Value, 
                        gpsLatitudeRef?.Value == "S");
                    metadata.Longitude = ConvertGpsCoordinate(
                        gpsLongitude.Value, 
                        gpsLongitudeRef?.Value == "W");
                }

                // Camera settings
                var iso = exif.GetValue(ExifTag.ISOSpeedRatings);
                metadata.IsoSpeed = iso?.Value;

                var fNumber = exif.GetValue(ExifTag.FNumber);
                metadata.FNumber = fNumber != null ? (double?)fNumber.Value : null;

                var exposure = exif.GetValue(ExifTag.ExposureTime);
                metadata.ExposureTime = exposure != null ? (double?)exposure.Value : null;

                var focalLength = exif.GetValue(ExifTag.FocalLength);
                metadata.FocalLength = focalLength != null ? (double?)focalLength.Value : null;

                // Orientation
                var orientation = exif.GetValue(ExifTag.Orientation);
                metadata.Orientation = orientation?.Value;
            }

            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting image metadata");
            return metadata;
        }
    }

    public async Task<Stream> ResizeImageAsync(Stream imageData, string mimeType, int maxWidth, int maxHeight)
    {
        try
        {
            imageData.Position = 0;
            using var image = await Image.LoadAsync(imageData);

            if (image.Width > maxWidth || image.Height > maxHeight)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(maxWidth, maxHeight),
                    Mode = ResizeMode.Max
                }));
            }

            var outputStream = new MemoryStream();
            await SaveImageAsync(image, outputStream, mimeType, 95);

            outputStream.Position = 0;
            return outputStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resizing image");
            throw;
        }
    }

    public async Task<Stream> CreateThumbnailAsync(Stream imageData, string mimeType, int size)
    {
        try
        {
            imageData.Position = 0;
            using var image = await Image.LoadAsync(imageData);

            // Create square thumbnail with crop
            var minDimension = Math.Min(image.Width, image.Height);
            
            image.Mutate(x => x
                .Resize(new ResizeOptions
                {
                    Size = new Size(size, size),
                    Mode = ResizeMode.Crop
                }));

            var outputStream = new MemoryStream();
            
            // Always save thumbnails as JPEG for consistency
            await image.SaveAsJpegAsync(outputStream, new JpegEncoder { Quality = 85 });

            outputStream.Position = 0;
            return outputStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating thumbnail");
            throw;
        }
    }

    private static string DetermineOutputMimeType(string originalMimeType, OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Jpeg => "image/jpeg",
            OutputFormat.Png => "image/png",
            OutputFormat.WebP => "image/webp",
            _ => originalMimeType
        };
    }

    private static async Task SaveImageAsync(Image image, Stream outputStream, string mimeType, int quality)
    {
        switch (mimeType.ToLowerInvariant())
        {
            case "image/jpeg":
                await image.SaveAsJpegAsync(outputStream, new JpegEncoder { Quality = quality });
                break;
            case "image/png":
                var pngOptions = new PngEncoder
                {
                    CompressionLevel = quality > 80 ? PngCompressionLevel.BestSpeed : PngCompressionLevel.BestCompression
                };
                await image.SaveAsPngAsync(outputStream, pngOptions);
                break;
            case "image/webp":
                await image.SaveAsWebpAsync(outputStream, new WebpEncoder { Quality = quality });
                break;
            default:
                await image.SaveAsJpegAsync(outputStream, new JpegEncoder { Quality = quality });
                break;
        }
    }

    private static string? GetExifValue(ExifProfile exif, ExifTag<string> tag)
    {
        var value = exif.GetValue(tag);
        return value?.Value;
    }

    private static DateTime? ParseExifDateTime(string dateTimeStr)
    {
        // EXIF date format: "yyyy:MM:dd HH:mm:ss"
        try
        {
            var parts = dateTimeStr.Split(' ');
            if (parts.Length != 2) return null;

            var dateParts = parts[0].Split(':');
            var timeParts = parts[1].Split(':');

            if (dateParts.Length != 3 || timeParts.Length != 3) return null;

            return new DateTime(
                int.Parse(dateParts[0]),
                int.Parse(dateParts[1]),
                int.Parse(dateParts[2]),
                int.Parse(timeParts[0]),
                int.Parse(timeParts[1]),
                int.Parse(timeParts[2]));
        }
        catch
        {
            return null;
        }
    }

    private static double ConvertGpsCoordinate(double[] coordinates, bool isNegative)
    {
        if (coordinates == null || coordinates.Length < 3) return 0;

        var degrees = coordinates[0];
        var minutes = coordinates[1];
        var seconds = coordinates[2];

        var decimalDegrees = degrees + (minutes / 60) + (seconds / 3600);
        return isNegative ? -decimalDegrees : decimalDegrees;
    }
}
