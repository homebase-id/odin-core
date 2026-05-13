using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Core.Storage;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// Bundles the V2 drive client wrappers (read + write) for a caller. Constructed once per caller
/// at session-build time so test bodies don't repeat <c>new DriveWriterV2Client(...)</c>.
/// </summary>
public sealed class DriveHandles
{
    public DriveReaderV2Client Reader { get; }
    public DriveWriterV2Client Writer { get; }

    public DriveHandles(OdinId identity, IApiClientFactory factory, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        Reader = new DriveReaderV2Client(identity, factory);
        Writer = new DriveWriterV2Client(identity, factory, fileSystemType);
    }
}
