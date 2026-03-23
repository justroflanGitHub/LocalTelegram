using Minio;
using Minio.DataModel.Args;

namespace FileService.Services;

public class MinioConfiguration
{
    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = "minioadmin";
    public string SecretKey { get; set; } = "minioadmin123";
    public bool UseSSL { get; set; }
}

public interface IStorageService
{
    Task EnsureBucketExistsAsync();
    Task<string> UploadFileAsync(string objectName, Stream data, long size, string? contentType);
    Task<Stream> DownloadFileAsync(string objectName);
    Task<string> GetPresignedUrlAsync(string objectName, int expirySeconds = 3600);
    Task DeleteFileAsync(string objectName);
    Task<bool> FileExistsAsync(string objectName);
}

public class MinioStorageService : IStorageService
{
    private readonly IMinioClient _minioClient;
    private readonly MinioConfiguration _config;
    private readonly ILogger<MinioStorageService> _logger;
    private const string BucketName = "localtelegram-files";

    public MinioStorageService(MinioConfiguration config, ILogger<MinioStorageService> logger)
    {
        _config = config;
        _logger = logger;

        _minioClient = new MinioClient()
            .WithEndpoint(config.Endpoint)
            .WithCredentials(config.AccessKey, config.SecretKey)
            .WithSSL(config.UseSSL)
            .Build();

        _logger.LogInformation("MinIO client configured for endpoint {Endpoint}", config.Endpoint);
    }

    public async Task EnsureBucketExistsAsync()
    {
        try
        {
            var bucketExistsArgs = new BucketExistsArgs()
                .WithBucket(BucketName);

            var exists = await _minioClient.BucketExistsAsync(bucketExistsArgs);

            if (!exists)
            {
                var makeBucketArgs = new MakeBucketArgs()
                    .WithBucket(BucketName);

                await _minioClient.MakeBucketAsync(makeBucketArgs);
                _logger.LogInformation("Created bucket {BucketName}", BucketName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure bucket exists");
            throw;
        }
    }

    public async Task<string> UploadFileAsync(string objectName, Stream data, long size, string? contentType)
    {
        try
        {
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(BucketName)
                .WithObject(objectName)
                .WithStreamData(data)
                .WithObjectSize(size)
                .WithContentType(contentType ?? "application/octet-stream");

            await _minioClient.PutObjectAsync(putObjectArgs);

            _logger.LogInformation("Uploaded file {ObjectName} to bucket {BucketName}", objectName, BucketName);

            return objectName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {ObjectName}", objectName);
            throw;
        }
    }

    public async Task<Stream> DownloadFileAsync(string objectName)
    {
        try
        {
            var memoryStream = new MemoryStream();

            var getObjectArgs = new GetObjectArgs()
                .WithBucket(BucketName)
                .WithObject(objectName)
                .WithCallbackStream((stream, cancellationToken) =>
                {
                    return stream.CopyToAsync(memoryStream, cancellationToken);
                });

            await _minioClient.GetObjectAsync(getObjectArgs);
            memoryStream.Position = 0;

            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file {ObjectName}", objectName);
            throw;
        }
    }

    public async Task<string> GetPresignedUrlAsync(string objectName, int expirySeconds = 3600)
    {
        try
        {
            var presignedGetObjectArgs = new PresignedGetObjectArgs()
                .WithBucket(BucketName)
                .WithObject(objectName)
                .WithExpiry(expirySeconds);

            var url = await _minioClient.PresignedGetObjectAsync(presignedGetObjectArgs);
            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get presigned URL for {ObjectName}", objectName);
            throw;
        }
    }

    public async Task DeleteFileAsync(string objectName)
    {
        try
        {
            var removeObjectArgs = new RemoveObjectArgs()
                .WithBucket(BucketName)
                .WithObject(objectName);

            await _minioClient.RemoveObjectAsync(removeObjectArgs);

            _logger.LogInformation("Deleted file {ObjectName} from bucket {BucketName}", objectName, BucketName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file {ObjectName}", objectName);
            throw;
        }
    }

    public async Task<bool> FileExistsAsync(string objectName)
    {
        try
        {
            var statObjectArgs = new StatObjectArgs()
                .WithBucket(BucketName)
                .WithObject(objectName);

            await _minioClient.StatObjectAsync(statObjectArgs);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
