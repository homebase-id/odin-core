using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public sealed class DiskFileStore(FileReaderWriter frw) : IDriveFileStore
{
    public StorageBackendType Backend => StorageBackendType.Disk;

    public Task<uint> WriteStreamAsync(string path, Stream stream, CancellationToken ct = default)
        => frw.WriteStreamAsync(path, stream);

    public async Task WriteBytesAsync(string path, byte[] bytes, CancellationToken ct = default)
        => await frw.WriteAllBytesAsync(path, bytes, ct);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default)
        => frw.GetAllFileBytesAsync(path);

    public Task<byte[]> ReadBytesAsync(string path, long start, long length, CancellationToken ct = default)
        => frw.GetFileBytesAsync(path, start, length, ct);

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        => Task.FromResult(frw.FileExists(path));

    public Task<long> LengthAsync(string path, CancellationToken ct = default)
        => Task.FromResult(new FileInfo(path).Length);

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        frw.DeleteFile(path);
        return Task.CompletedTask;
    }

    public Task DeleteSetAsync(string dir, Guid fileId, CancellationToken ct = default)
    {
        if (Directory.Exists(dir))
            frw.DeleteFiles(Directory.GetFiles(dir, $"{fileId:N}.*"));
        return Task.CompletedTask;
    }

    public Task EnsureDirectoryAsync(string dir, CancellationToken ct = default)
    {
        frw.CreateDirectory(dir);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Promotes a staged file into this disk store. The destination is disk, so the only valid case is a
    /// Disk -&gt; Disk local copy.
    /// </summary>
    /// <remarks>
    /// A non-disk (S3) source would be an S3 -&gt; Disk promote, which the all-or-nothing storage rule
    /// guarantees never happens (uploads are always disk; the inbox and payload areas share one S3 switch).
    /// We therefore treat it as a programming error and throw rather than risk a silent mis-copy.
    /// <paramref name="sourcePath"/> and <paramref name="destPath"/> are both local file system paths.
    /// </remarks>
    public Task CopyFromAsync(IDriveFileStore source, string sourcePath, string destPath, CancellationToken ct = default)
    {
        if (source.Backend != StorageBackendType.Disk)
            throw new DriveFileStoreException($"Disk dest cannot ingest from {source.Backend} source");

        // Disk -> Disk. CopyPayloadFile creates the destination directory and verifies the copied size.
        frw.CopyPayloadFile(sourcePath, destPath);
        return Task.CompletedTask;
    }

    public (string bucket, string fullKey)? GetS3Location(string relativePath) => null;
}
