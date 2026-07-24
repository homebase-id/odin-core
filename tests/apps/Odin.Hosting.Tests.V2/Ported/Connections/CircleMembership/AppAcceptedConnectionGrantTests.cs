#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// An app (no master key) accepting an incoming connection request mints a "keyless" PeerKeyStore:
/// <c>MasterKeyEncryptedPeerKey</c> is null and the Peer Key survives only as the ECC-encrypted
/// <c>TempWeakKeyStoreKey</c> (<c>CircleNetworkRequestService.AcceptConnectionRequestAsync</c>).
/// <c>CircleNetworkService.GrantCircleAsync</c>'s owner branch must run the deferred master-key
/// upgrade before decrypting the Peer Key — without it the owner's next circles/add threw an NRE.
/// </summary>
[TestFixture]
public class AppAcceptedConnectionGrantTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task OwnerCanGrantCircle_AfterAppAcceptedTheConnectionRequest()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var appDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(appDrive, "appDrive", allowAnonymousReads: false);

        // UseTransitWrite is what puts the ICR key in the app's permission context — required to
        // decrypt the incoming pending request (it's ECC-encrypted under the OnlineIcrEncryptedKey).
        var app = await AppSession.SetupAsync(frodo, appDrive, DrivePermission.Read,
            permissionKeys: new[]
            {
                PermissionKeys.ReadConnectionRequests,
                PermissionKeys.ManageCircleMembership,
                PermissionKeys.UseTransitWrite
            });

        // Sam asks to connect; the app on Frodo's side — not the owner — accepts.
        var sendReq = await new UniversalCircleNetworkRequestsApiClient(sam.Identity, sam.Factory)
            .SendConnectionRequest(frodo.Identity);
        Assert.That(sendReq.IsSuccessStatusCode, Is.True, $"SendConnectionRequest failed: {sendReq.StatusCode}");

        var accept = await new V2ConnectionRequestsClient(app.Identity, app.Factory)
            .AcceptIncomingRequestAsync(sam.Identity);
        Assert.That(accept.IsSuccessStatusCode, Is.True, $"app accept failed: {accept.StatusCode} {accept.Error?.Content}");

        var storage = Host.GetTenantScope(frodo.Identity.DomainName).Resolve<CircleNetworkStorage>();
        var before = await storage.GetAsync(sam.Identity);
        Assert.That(before, Is.Not.Null, "frodo should hold an ICR for sam after the app accepted");
        Assert.That(before!.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade(), Is.True,
            "precondition: an app-accepted connection has no master-key-encrypted peer key");

        var circleA = Guid.NewGuid();
        var created = await frodo.Admin.CreateCircle(circleA, "circleA", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = appDrive, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        });
        Assert.That(created.IsSuccessStatusCode, Is.True, $"CreateCircle failed: {created.StatusCode}");

        // The owner grant: previously a 500 (NullReferenceException on MasterKeyEncryptedPeerKey).
        var grant = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory)
            .GrantCircleAsync(circleA, sam.Identity);
        Assert.That(grant.IsSuccessStatusCode, Is.True, $"owner GrantCircle failed: {grant.StatusCode}");

        var after = await storage.GetAsync(sam.Identity);
        Assert.That(after!.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade(), Is.False,
            "the grant should have upgraded the peer key to master-key encryption");
        Assert.That(after.PeerKeyStore.CircleGrants.ContainsKey(circleA), Is.True,
            "sam should hold a real CircleGrant for circleA (owner branch, not a deposit)");
        Assert.That(after.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circleA), Is.False,
            "the owner path grants directly; nothing should be left pending");

        var members = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory).GetCircleMembersAsync(circleA);
        Assert.That(members.IsSuccessStatusCode, Is.True, $"GetCircleMembers failed: {members.StatusCode}");
        Assert.That(members.Content!.Any(m => m == sam.Identity), Is.True, "sam should appear as a member of circleA");
    }
}
