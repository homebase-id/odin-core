using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Core.Storage.ObjectStorage;

#nullable enable

public sealed record S3ObjectInfo(string Key, DateTimeOffset LastModified, long Size);

public interface IS3Storage
{
    string BucketName { get; }
    Task CreateBucketAsync(CancellationToken cancellationToken = default);
    Task<bool> BucketExistsAsync(CancellationToken cancellationToken = default);
    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default);
    Task WriteBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default);
    Task<long> WriteStreamAsync(string path, System.IO.Stream stream, CancellationToken cancellationToken = default);
    Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellationToken = default);
    Task<byte[]> ReadBytesAsync(string path, long offset, long length, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
    Task CopyFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task MoveFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task UploadFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task DownloadFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task<long> FileLengthAsync(string filePath, CancellationToken cancellationToken = default);

    // Ensure (days > 0) or remove (days <= 0) an S3 lifecycle expiration rule scoped to this
    // storage's root prefix. Idempotent.
    Task EnsureExpirationLifecycleAsync(int expirationDays, CancellationToken cancellationToken = default);

    /// Returns the full S3 key (rootPath + path) for a relative path, without touching S3.
    string GetFullKey(string path);

    /// Server-side copy from a different bucket into this store's bucket.
    /// <paramref name="sourceBucket"/> and <paramref name="sourceKey"/> are the raw source coordinates
    /// (sourceKey must already be the full key including the source store's rootPath — do NOT let
    /// this store apply its own rootPath to the source).
    /// <paramref name="destKey"/> is relative; this store applies its own rootPath to it.
    Task CopyFromBucketAsync(string sourceBucket, string sourceKey, string destKey, CancellationToken cancellationToken = default);
}

