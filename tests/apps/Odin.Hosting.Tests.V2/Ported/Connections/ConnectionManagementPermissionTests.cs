#nullable enable
using System.Collections;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Verifies the authorization boundary on the connection-management operations whose service-layer
/// gate was changed from owner+master-key (AssertCanManageConnections) to
/// AssertHasPermission(PermissionKeys.ManageContacts):
///   - Block        (CircleNetworkService.BlockAsync)
///   - Disconnect   (CircleNetworkService.DisconnectAsync)
///   - CancelOutgoing / DeleteSentRequest (CircleNetworkRequestService.DeleteSentRequest)
///
/// The permission assert runs at the top of each service method, before any state logic, and all
/// three operations are no-op-safe (block-stranger -> Blocked, disconnect-when-not-connected -> 200,
/// delete-nonexistent-sent-request -> 204). So we exercise the gate directly without establishing a
/// connection: the only thing varying the outcome is the caller's permission set. Allowed callers
/// get a 2xx (200 for block/disconnect, 204 for the request-delete); a caller lacking ManageContacts
/// gets 403.
/// </summary>
public class ConnectionManagementPermissionTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo];

    private static readonly OdinId Target = (OdinId)Identities.Sam;

    // permissionKeys == null  => Owner caller (owner holds PermissionKeys.All, includes ManageContacts)
    // permissionKeys != null  => App caller granted exactly those keys
    public static IEnumerable AllowedCallers()
    {
        yield return new object[] { "Owner", default(int[])! };
        yield return new object[] { "App[ManageContacts]", new[] { PermissionKeys.ManageContacts } };
    }

    public static IEnumerable DeniedCallers()
    {
        yield return new object[] { "App[ReadConnections]", new[] { PermissionKeys.ReadConnections } };
        yield return new object[] { "App[none]", new int[0] };
    }

    private async Task<IV2Caller> BuildCallerAsync(OwnerSession owner, int[]? appPermissionKeys)
    {
        if (appPermissionKeys is null)
        {
            return owner;
        }

        // The app caller needs a drive grant to register against; the drive itself is irrelevant to
        // these connection operations, so use a throwaway drive with no drive permission.
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "perm-test-drive", allowAnonymousReads: false);
        return await AppSession.SetupAsync(owner, drive, DrivePermission.None, appPermissionKeys);
    }

    [Test]
    [TestCaseSource(nameof(AllowedCallers))]
    public async Task Block_Allowed_WhenCallerHasManageContacts(string label, int[]? appPermissionKeys)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var caller = await BuildCallerAsync(owner, appPermissionKeys);
        var client = new V2ConnectionNetworkClient(caller.Identity, caller.Factory);

        var response = await client.BlockAsync(Target);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"{label}: expected 2xx, got {response.StatusCode}");
    }

    [Test]
    [TestCaseSource(nameof(DeniedCallers))]
    public async Task Block_Forbidden_WhenCallerLacksManageContacts(string label, int[]? appPermissionKeys)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var caller = await BuildCallerAsync(owner, appPermissionKeys);
        var client = new V2ConnectionNetworkClient(caller.Identity, caller.Factory);

        var response = await client.BlockAsync(Target);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"{label}: got {response.StatusCode}");
    }

    [Test]
    [TestCaseSource(nameof(AllowedCallers))]
    public async Task Disconnect_Allowed_WhenCallerHasManageContacts(string label, int[]? appPermissionKeys)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var caller = await BuildCallerAsync(owner, appPermissionKeys);
        var client = new V2ConnectionNetworkClient(caller.Identity, caller.Factory);

        var response = await client.DisconnectAsync(Target);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"{label}: expected 2xx, got {response.StatusCode}");
    }

    [Test]
    [TestCaseSource(nameof(DeniedCallers))]
    public async Task Disconnect_Forbidden_WhenCallerLacksManageContacts(string label, int[]? appPermissionKeys)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var caller = await BuildCallerAsync(owner, appPermissionKeys);
        var client = new V2ConnectionNetworkClient(caller.Identity, caller.Factory);

        var response = await client.DisconnectAsync(Target);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"{label}: got {response.StatusCode}");
    }

    [Test]
    [TestCaseSource(nameof(AllowedCallers))]
    public async Task CancelOutgoingRequest_Allowed_WhenCallerHasManageContacts(string label, int[]? appPermissionKeys)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var caller = await BuildCallerAsync(owner, appPermissionKeys);
        var client = new V2ConnectionRequestsClient(caller.Identity, caller.Factory);

        var response = await client.CancelOutgoingRequestAsync(Target);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"{label}: expected 2xx, got {response.StatusCode}");
    }

    [Test]
    [TestCaseSource(nameof(DeniedCallers))]
    public async Task CancelOutgoingRequest_Forbidden_WhenCallerLacksManageContacts(string label, int[]? appPermissionKeys)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var caller = await BuildCallerAsync(owner, appPermissionKeys);
        var client = new V2ConnectionRequestsClient(caller.Identity, caller.Factory);

        var response = await client.CancelOutgoingRequestAsync(Target);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"{label}: got {response.StatusCode}");
    }
}
