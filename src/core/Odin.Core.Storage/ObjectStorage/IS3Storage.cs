using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Core.Storage.ObjectStorage;

#nullable enable

public interface IS3Storage
{
    string BucketName { get; }
    Task<bool> BucketExistsAsync(CancellationToken cancellationToken = default);
    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default);
    Task WriteBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default);
    Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellationToken = default);
    Task<byte[]> ReadBytesAsync(string path, long offset, long length, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string path, CancellationToken cancellationToken = default);
    Task CopyFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task MoveFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task<List<string>> ListFilesAsync(string path, bool recursive = false, CancellationToken cancellationToken = default);
    Task UploadFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
    Task DownloadFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default);
}
