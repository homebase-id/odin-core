using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Hosting;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// Common surface for any V2 caller (Owner, App, Guest). Tests parameterized across caller types
/// accept this; <see cref="Factory"/> is the escape hatch for building V2 clients we haven't yet
/// wrapped as first-class properties (e.g. <c>DriveWriterV2Client</c>, <c>DriveReaderV2Client</c>).
/// </summary>
public interface IV2Caller
{
    OdinId Identity { get; }
    InProcessApiClientFactory Factory { get; }
    AuthV2Client Auth { get; }
    DriveHandles Drives { get; }

    /// <summary>V1-shaped clients for fixtures whose system under test is a V1 endpoint.</summary>
    V1Handles V1 { get; }
}
