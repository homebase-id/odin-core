using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Notifications;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// Bundles the V1-shaped client wrappers for a caller, mirroring <see cref="DriveHandles"/>. The V1
/// endpoints are reached through the caller's own V1 base path (see
/// <c>InProcessApiClientFactory.V1PathNormalizingHandler</c>), so a handle built here is already
/// pointed at the right surface for that caller — pairing one caller's identity with another's
/// factory, which hand-rolling <c>new UniversalXApiClient(identity, factory)</c> per test makes easy,
/// is not expressible.
/// </summary>
/// <remarks>
/// Exists for the <c>_Universal</c> migration: fixtures whose system under test is a V1 endpoint use
/// these, while their V2 counterparts use <see cref="DriveHandles"/>.
///
/// Not by itself the V1-retirement checklist: the suite also reaches V1 through
/// <see cref="OwnerAdmin"/> (drive management, apps, circles, YouAuth domains) and, where a refusal
/// is under test, through raw Refit interfaces via <see cref="OwnerSession.RefitFor{T}"/>. These
/// handles cover the per-caller drive / reaction / static-file / notification clients only.
/// </remarks>
public sealed class V1Handles
{
    public UniversalDriveApiClient Drive { get; }
    public UniversalDriveReactionClient Reactions { get; }
    public UniversalStaticFileApiClient StaticFiles { get; }
    public AppNotificationsApiClient Notifications { get; }

    public V1Handles(OdinId identity, IApiClientFactory factory)
    {
        Drive = new UniversalDriveApiClient(identity, factory);
        Reactions = new UniversalDriveReactionClient(identity, factory);
        StaticFiles = new UniversalStaticFileApiClient(identity, factory);
        Notifications = new AppNotificationsApiClient(identity, factory);
    }
}
