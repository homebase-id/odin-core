using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

// TODO:INBOX Delete this store (and its DI registration in TenantServices) once the inbox folder is drained.
// It only routes I/O to the per-drive inbox folder; nothing should target that folder afterward.
public sealed class InboxFileStore(IDriveFileStore inner) : IDriveFileStore
{
    public StorageBackendType Backend => inner.Backend;
    public Task<uint> WriteStreamAsync(string p, Stream s, CancellationToken ct = default) => inner.WriteStreamAsync(p, s, ct);
    public Task WriteBytesAsync(string p, byte[] b, CancellationToken ct = default) => inner.WriteBytesAsync(p, b, ct);
    public Task<byte[]> ReadAllBytesAsync(string p, CancellationToken ct = default) => inner.ReadAllBytesAsync(p, ct);
    public Task<byte[]> ReadBytesAsync(string p, long s, long l, CancellationToken ct = default) => inner.ReadBytesAsync(p, s, l, ct);
    public Task<bool> ExistsAsync(string p, CancellationToken ct = default) => inner.ExistsAsync(p, ct);
    public Task<long> LengthAsync(string p, CancellationToken ct = default) => inner.LengthAsync(p, ct);
    public Task DeleteAsync(string p, CancellationToken ct = default) => inner.DeleteAsync(p, ct);
    public Task DeleteSetAsync(string d, Guid f, CancellationToken ct = default) => inner.DeleteSetAsync(d, f, ct);
    public Task EnsureDirectoryAsync(string d, CancellationToken ct = default) => inner.EnsureDirectoryAsync(d, ct);
    public Task IngestFromAsync(IDriveFileStore src, string s, string d, CancellationToken ct = default) => inner.IngestFromAsync(src, s, d, ct);
    public (string bucket, string fullKey)? GetS3Location(string relativePath) => inner.GetS3Location(relativePath);
}
