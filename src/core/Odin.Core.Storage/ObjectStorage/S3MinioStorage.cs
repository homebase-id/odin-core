#if ODIN_MINIO_STORAGE
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;

namespace Odin.Core.Storage.ObjectStorage;

#nullable enable

// SEB:TOO TryRetry here or in callers?

public class S3MinioStorage : IS3Storage
{
    private readonly ILogger _logger;
    private readonly IMinioClient _minioClient;

    public string BucketName { get; }

    public S3MinioStorage(ILogger logger, IMinioClient minioClient, string bucketName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName, nameof(bucketName));
        _logger = logger;
        _minioClient = minioClient;
        BucketName = bucketName;
    }

    //

    public Task<bool> BucketExistsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogTrace(nameof(BucketExistsAsync));
        return _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(BucketName), cancellationToken);
    }

    //

    public async Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        S3Path.AssertFileName(path);
        path = S3Path.Combine(path);

        try
        {
            await _minioClient.StatObjectAsync(
                new StatObjectArgs().WithBucket(BucketName).WithObject(path),
                cancellationToken);
            return true;
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            return false;
        }
    }

    //

    public async Task WriteBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace(nameof(WriteBytesAsync));

        S3Path.AssertFileName(path);
        path = S3Path.Combine(path);

        var memoryStream = new MemoryStream(bytes);
        try
        {
            var putArgs = new PutObjectArgs()
                .WithBucket(BucketName)
                .WithObject(path)
                .WithStreamData(memoryStream)
                .WithObjectSize(bytes.Length)
                .WithContentType("application/octet-stream");

            await _minioClient.PutObjectAsync(putArgs, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write object '{Path}' to bucket '{Bucket}': {Message}",
                path, BucketName, ex.Message);
            throw;
        }
        finally
        {
            await memoryStream.DisposeAsync();
        }
    }

    //

    public async Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        S3Path.AssertFileName(path);
        path = S3Path.Combine(path);

        var memoryStream = new MemoryStream();
        try
        {
            var getArgs = new GetObjectArgs()
                .WithBucket(BucketName)
                .WithObject(path)
                .WithCallbackStream(async (stream, ct) =>
                {
                    await stream.CopyToAsync(memoryStream, ct);
                });

            await _minioClient.GetObjectAsync(getArgs, cancellationToken);
            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to read object '{Path}' from bucket '{Bucket}': {Message}", path, BucketName, ex.Message);
            throw;
        }
        finally
        {
            await memoryStream.DisposeAsync();
        }
    }

    //

    public async Task<byte[]> ReadBytesAsync(string path, long offset, long length, CancellationToken cancellationToken = default)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative");
        }

        if (length < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Length must be greater than 0");
        }

        if (length == long.MaxValue)
        {
            length -= offset;
        }

        S3Path.AssertFileName(path);
        path = S3Path.Combine(path);

        // SEB:NOTE We need to check if the requested range is valid before reading the object.
        // Hopefully minio will fix this so we don't need the extra roundtrip.
        // Github issue: https://github.com/minio/minio-dotnet/issues/1309
        var statArgs = new StatObjectArgs()
            .WithBucket(BucketName)
            .WithObject(path);

        var objectStat = await _minioClient.StatObjectAsync(statArgs, cancellationToken);
        var objectSize = objectStat.Size;

        if (offset >= objectSize)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset is greater than the size of the object");
        }

        var maxAvailableLength = objectSize - offset;
        if (length > maxAvailableLength)
        {
            length = maxAvailableLength;
        }

        var memoryStream = new MemoryStream();
        try
        {
            var getArgs = new GetObjectArgs()
                .WithBucket(BucketName)
                .WithObject(path)
                .WithOffsetAndLength(offset, length)
                .WithCallbackStream(async (stream, ct) =>
                {
                    await stream.CopyToAsync(memoryStream, ct);
                });

            await _minioClient.GetObjectAsync(getArgs, cancellationToken);
            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to read object '{Path}' from bucket '{Bucket}' with offset {Offset} and length {Length}: {Message}",
                path, BucketName, offset, length, ex.Message);
            throw;
        }
        finally
        {
            await memoryStream.DisposeAsync();
        }
    }

    //

    public async Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
    {
        S3Path.AssertFileName(path);
        path = S3Path.Combine(path);

        try
        {
            var removeArgs = new RemoveObjectArgs()
                .WithBucket(BucketName)
                .WithObject(path);
            await _minioClient.RemoveObjectAsync(removeArgs, cancellationToken);
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            // Ignore
        }
    }

    //

    public async Task CopyFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default)
    {
        S3Path.AssertFileName(srcPath);
        S3Path.AssertFileName(dstPath);
        srcPath = S3Path.Combine(srcPath);
        dstPath = S3Path.Combine(dstPath);

        var cpSrcArgs = new CopySourceObjectArgs()
            .WithBucket(BucketName)
            .WithObject(srcPath);

        var args = new CopyObjectArgs()
            .WithBucket(BucketName)
            .WithObject(dstPath)
            .WithCopyObjectSource(cpSrcArgs);

        await _minioClient.CopyObjectAsync(args, cancellationToken);
    }

    //

    public async Task MoveFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default)
    {
        await CopyFileAsync(srcPath, dstPath, cancellationToken);
        await DeleteFileAsync(srcPath, cancellationToken);
    }

    //

    public async Task UploadFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default)
    {
        S3Path.AssertFileName(dstPath);
        dstPath = S3Path.Combine(dstPath);

        await using var inputStream = new FileStream(srcPath, FileMode.Open, FileAccess.Read);

        var putObjectArgs = new PutObjectArgs()
            .WithBucket(BucketName)
            .WithStreamData(inputStream)
            .WithObjectSize(inputStream.Length)
            .WithObject(dstPath)
            .WithContentType("application/octet-stream");

        await _minioClient.PutObjectAsync(putObjectArgs, cancellationToken);
    }

    //

    public async Task DownloadFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default)
    {
        S3Path.AssertFileName(srcPath);
        srcPath = S3Path.Combine(srcPath);

        Directory.CreateDirectory(Path.GetDirectoryName(dstPath) ?? throw new InvalidOperationException());

        var getObjectArgs = new GetObjectArgs()
            .WithBucket(BucketName)
            .WithObject(srcPath)
            .WithFile(dstPath);

        await _minioClient.GetObjectAsync(getObjectArgs, cancellationToken);
    }

}

//

public static class S3MinioStorageExtensions
{
    public static IServiceCollection AddMinioClient(
        this IServiceCollection services,
        string endpoint,
        string accessKey,
        string secretAccessKey,
        string region)
    {
        services.AddSingleton<IMinioClient>(_ =>
            new MinioClient()
                .WithEndpoint(endpoint)
                .WithCredentials(accessKey, secretAccessKey)
                .WithRegion(region)
                .WithSSL()
                .Build());

        return services;
    }
}
#endif
