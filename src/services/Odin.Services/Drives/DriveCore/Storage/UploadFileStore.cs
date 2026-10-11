using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public sealed class UploadFileStore(IDriveFileStore inner) : IDriveFileStore
{
    public StorageBackendType Backend => inner.Backend;
    public Task<uint> WriteStreamAsync(string p, Stream s, CancellationToken ct = default) => inner.WriteStreamAsync(p, s, ct);
    public Task WriteBytesAsync(string p, byte[] b, CancellationToken ct = default) => inner.WriteBytesAsync(p, b, ct);
    public Task<Stream> OpenReadAsync(string p, Int64 start = 0, Int64? length = null, CancellationToken ct = default)
        => inner.OpenReadAsync(p, start, length, ct);
    public Task<bool> ExistsAsync(string p, CancellationToken ct = default) => inner.ExistsAsync(p, ct);
    public Task<long> LengthAsync(string p, CancellationToken ct = default) => inner.LengthAsync(p, ct);
    public Task DeleteAsync(string p, CancellationToken ct = default) => inner.DeleteAsync(p, ct);
    public Task DeleteSetAsync(string d, Guid f, CancellationToken ct = default) => inner.DeleteSetAsync(d, f, ct);
    public Task EnsureDirectoryAsync(string d, CancellationToken ct = default) => inner.EnsureDirectoryAsync(d, ct);
    public Task DeleteDirectoryAsync(string d, CancellationToken ct = default) => inner.DeleteDirectoryAsync(d, ct);
    public Task CopyFromAsync(IDriveFileStore src, string s, string d, CancellationToken ct = default) => inner.CopyFromAsync(src, s, d, ct);
    public (string bucket, string fullKey)? GetS3Location(string relativePath) => inner.GetS3Location(relativePath);
}
