using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public enum StorageBackendType { Disk, S3 }

/// Backend-agnostic blob I/O over a path (disk) or key (S3).
public interface IDriveFileStore
{
    StorageBackendType Backend { get; }

    Task<uint>   WriteStreamAsync(string path, Stream stream, CancellationToken ct = default);
    Task         WriteBytesAsync(string path, byte[] bytes, CancellationToken ct = default);
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default);
    Task<byte[]> ReadBytesAsync(string path, long start, long length, CancellationToken ct = default);
    Task<bool>   ExistsAsync(string path, CancellationToken ct = default);
    Task<long>   LengthAsync(string path, CancellationToken ct = default);
    Task         DeleteAsync(string path, CancellationToken ct = default);
    Task         DeleteSetAsync(string dir, Guid fileId, CancellationToken ct = default); // {fileId:N}.*
    Task         EnsureDirectoryAsync(string dir, CancellationToken ct = default);          // no-op on S3

    /// <summary>
    /// Promotes a staged file from <paramref name="source"/> into THIS store (the destination): it moves a
    /// file out of a temporary staging area (an upload or an inbox) into long-term storage at commit time.
    /// </summary>
    /// <remarks>
    /// Dispatches on the pair (source.Backend, this.Backend). Four combinations are conceivable; the
    /// all-or-nothing rule below rules one of them out:
    /// <list type="bullet">
    ///   <item><b>Disk -&gt; Disk</b>: a local file copy.</item>
    ///   <item><b>Disk -&gt; S3</b>: read the local file and upload (PUT) it into this S3 store. This is the
    ///         upload-staging case, where staging is on disk and long-term storage is on S3.</item>
    ///   <item><b>S3 -&gt; S3</b>: a server-side copy that never round-trips the bytes through this host, and
    ///         may be cross-bucket (e.g. the inbox bucket into the payload bucket). The source's bucket and
    ///         full object key come from <see cref="GetS3Location"/>.</item>
    ///   <item><b>S3 -&gt; Disk</b>: does NOT occur and is rejected as a programming error (see below).</item>
    /// </list>
    /// All-or-nothing invariant: uploads are always on disk, while the inbox and long-term payload areas
    /// follow a single per-tenant S3 switch. A tenant is therefore effectively all-disk or all-S3 for the
    /// switched areas, so the only source-to-destination pairs that actually arise are Disk-to-Disk,
    /// Disk-to-S3, and S3-to-S3. An S3 source promoting into a disk destination cannot happen under that rule.
    ///
    /// Why dispatch on the SOURCE backend at all: promotion used to read the source from local disk
    /// unconditionally, which silently failed the moment inbox staging could live on S3 (the source was no
    /// longer on disk). Choosing the operation from the source's backend is what closes that bug.
    ///
    /// Path semantics: <paramref name="sourcePath"/> identifies the file within <paramref name="source"/> and
    /// <paramref name="destPath"/> identifies it within this store. For an S3-backed store these are
    /// root-relative keys (the store applies its own bucket and root prefix); for a disk store they are file
    /// system paths.
    /// </remarks>
    Task CopyFromAsync(IDriveFileStore source, string sourcePath, string destPath, CancellationToken ct = default);

    /// <summary>
    /// Returns the (bucket, full object key) of <paramref name="relativePath"/> in THIS store, or
    /// <c>null</c> when this store is not S3-backed (a disk store has no bucket or key). The returned key
    /// already includes this store's root prefix, so it is usable as-is as a copy source. An S3 destination's
    /// <see cref="CopyFromAsync"/> calls this on the source store to perform the S3-to-S3 (cross-bucket)
    /// server-side copy.
    /// </summary>
    (string bucket, string fullKey)? GetS3Location(string relativePath);
}

public class DriveFileStoreException : OdinSystemException
{
    public DriveFileStoreException(string message) : base(message) { }
    public DriveFileStoreException(string message, Exception inner) : base(message, inner) { }
}
