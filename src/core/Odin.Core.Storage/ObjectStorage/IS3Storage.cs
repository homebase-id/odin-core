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
    Task<uint> WriteStreamAsync(string path, System.IO.Stream stream, CancellationToken cancellationToken = default);
    Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellationToken = default);
    Task<byte[]> ReadBytesAsync(string path, long offset, long length, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default);
    Task CopyFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task MoveFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task UploadFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task DownloadFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task<long> FileLengthAsync(string filePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<S3ObjectInfo>> ListAsync(string prefix, CancellationToken cancellationToken = default);
    Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    // Returns the full in-bucket object key for a store-relative path (applies this store's root prefix).
    string ResolveObjectKey(string path);

    // Server-side copy between two ALREADY-RESOLVED absolute object keys in THIS bucket (no root prefixing).
    // Used to promote an inbox-staged object to payload long-term storage within one bucket.
    Task CopyObjectAsync(string sourceObjectKey, string destObjectKey, CancellationToken cancellationToken = default);
}

