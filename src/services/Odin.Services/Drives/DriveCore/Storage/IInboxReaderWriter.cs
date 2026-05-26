using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

// Storage abstraction for peer inbox staging files. Disk-backed (InboxFileReaderWriter)
// or S3-backed (InboxS3ReaderWriter), selected by config in TenantServices.
// Paths are filesystem paths on disk, S3 object keys on S3 — callers stay backend-agnostic.
public interface IInboxReaderWriter
{
    Task<uint> WriteStreamAsync(string filePath, Stream stream, CancellationToken cancellationToken = default);
    Task<byte[]> GetFileBytesAsync(string filePath, CancellationToken cancellationToken = default);
    Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);

    // No-op on S3 (no directories). Disk creates the directory.
    Task EnsureDirectoryAsync(string dir, CancellationToken cancellationToken = default);

    // Deletes every staged part for a single inbox fileId. pathPrefix is
    // "<driveInboxPath>/<fileId:N>." — disk globs "<fileId:N>.*", S3 deletes by key prefix.
    Task DeleteByPrefixAsync(string pathPrefix, CancellationToken cancellationToken = default);

    // Promotes an inbox-staged object to a resolved destination (payload long-term storage).
    // For S3 this is a same-bucket server-side CopyObject; for disk a file copy. Only invoked when
    // the source is the inbox (caller gates on S3InboxEnabled for the S3 case).
    Task PromoteToAsync(string inboxRelativePath, string destResolvedKey, CancellationToken cancellationToken = default);
}

public class InboxReaderWriterException : OdinSystemException
{
    public InboxReaderWriterException(string message) : base(message) { }
    public InboxReaderWriterException(string message, System.Exception innerException) : base(message, innerException) { }
}
