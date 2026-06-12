using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives.FileSystem.Base;

/// <summary>
/// Echoes the server-assigned identity of a payload uploaded in this request.  The
/// <see cref="LastModified"/> value is sourced from the same <see cref="PayloadDescriptor"/>
/// instance stored in the file header, so it matches fileMetadata.payloads[].lastModified exactly.
/// </summary>
public class PayloadUploadReceipt
{
    public string Key { get; init; }

    public UnixTimeUtcUnique Uid { get; init; }

    public UnixTimeUtc LastModified { get; init; }

    public static PayloadUploadReceipt From(PayloadDescriptor descriptor) => new()
    {
        Key = descriptor.Key,
        Uid = descriptor.Uid,
        LastModified = descriptor.LastModified
    };
}
