using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Storage.ObjectStorage;
using Odin.Test.Helpers;

namespace Odin.Core.Storage.Tests.ObjectStorage;

#nullable enable

/// Test-only whole reads. Production code has none: an object is unbounded, so it streams (#1892).
public static class S3StorageTestExtensions
{
    public static Task<byte[]> ReadBytesAsync(this IS3Storage storage, string path, CancellationToken ct = default)
        => storage.ReadBytesAsync(path, 0, null, ct);

    public static async Task<byte[]> ReadBytesAsync(this IS3Storage storage, string path, Int64 start, Int64? length,
        CancellationToken ct = default)
    {
        await using var stream = await storage.OpenReadAsync(path, start, length, ct);
        return await stream.ReadToEndAsync(ct);
    }
}
