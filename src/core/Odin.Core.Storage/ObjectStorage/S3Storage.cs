using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Odin.Core.Exceptions;

namespace Odin.Core.Storage.ObjectStorage;

#nullable enable

public interface IS3Storage
{
    string BucketName { get; }
    string RootPath { get; }
    Task<bool> BucketExistsAsync(CancellationToken cancellationToken = default);
    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default);
    Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default);
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string path, CancellationToken cancellationToken = default);
    Task CopyFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task MoveFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task<List<string>> ListFilesAsync(string path, bool recursive = false, CancellationToken cancellationToken = default);
}

//

// SEB:TOO TryRetry here or in callers?

public class S3Storage : IS3Storage
{
    private readonly ILogger _logger;
    private readonly IMinioClient _minioClient;

    public string BucketName { get; }
    public string RootPath { get; }

    public S3Storage(ILogger logger, IMinioClient minioClient, string bucketName, string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName, nameof(bucketName));
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath, nameof(rootPath));

        _logger = logger;
        _minioClient = minioClient;
        BucketName = bucketName;
        RootPath = rootPath;
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

        path = S3Path.Combine(RootPath, path);
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

    public async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace(nameof(WriteAllBytesAsync));

        S3Path.AssertFileName(path);
        path = S3Path.Combine(RootPath, path);

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

    public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace(nameof(ReadAllBytesAsync));

        S3Path.AssertFileName(path);
        path = S3Path.Combine(RootPath, path);

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
            _logger.LogError(ex, "Failed to read object '{Path}' from bucket '{Bucket}': {Message}",
                path, BucketName, ex.Message);
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
        path = S3Path.Combine(RootPath, path);

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

        srcPath = S3Path.Combine(RootPath, srcPath);
        dstPath = S3Path.Combine(RootPath, dstPath);

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

    public async Task<List<string>> ListFilesAsync(
        string path,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        S3Path.AssertFolderName(path);

        path = S3Path.Combine(RootPath, path);

        var result = new List<string>();
        var listArgs = new ListObjectsArgs()
            .WithBucket(BucketName)
            .WithPrefix(path)
            .WithRecursive(recursive);

        await foreach (var item in _minioClient.ListObjectsEnumAsync(listArgs, cancellationToken))
        {
            var key = item.Key;

            // Sanity
            if (!key.StartsWith(RootPath))
            {
                throw new S3StorageException($"Key '{key}' does not start with root path '{RootPath}'");
            }

            key = key[RootPath.Length..];

            if (key != "" && !key.EndsWith('/'))
            {
                result.Add(key);
            }
        }

        return result;
    }

    //

}

//

public class S3StorageException : OdinSystemException
{
    public S3StorageException(string message) : base(message)
    {
    }

    public S3StorageException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

//

public static class S3StorageExtensions
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

