using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

#nullable enable

/// Test-only whole reads. Production code has none: a payload is unbounded, so it streams (#1892).
public static class DriveFileStoreTestExtensions
{
    public static async Task<byte[]> ReadAllBytesAsync(this IDriveFileStore store, string path, CancellationToken ct = default)
    {
        await using var stream = await store.OpenReadAsync(path, ct: ct);
        return await stream.ReadToEndAsync(ct);
    }

    public static async Task<byte[]> ReadBytesAsync(this IDriveFileStore store, string path, Int64 start, Int64? length,
        CancellationToken ct = default)
    {
        await using var stream = await store.OpenReadAsync(path, start, length, ct);
        return await stream.ReadToEndAsync(ct);
    }

    /// Reads to the end, and checks the stream yielded exactly the Length it promised.
    public static async Task<byte[]> ReadToEndAsync(this Stream stream, CancellationToken ct = default)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, ct);
        if (copy.Length != stream.Length)
        {
            throw new InvalidDataException($"Stream promised {stream.Length} bytes and yielded {copy.Length}");
        }
        return copy.ToArray();
    }
}
