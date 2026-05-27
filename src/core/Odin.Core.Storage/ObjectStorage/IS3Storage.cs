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
    Task CopyFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task MoveFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task UploadFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task DownloadFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task<long> FileLengthAsync(string filePath, CancellationToken cancellationToken = default);
}

