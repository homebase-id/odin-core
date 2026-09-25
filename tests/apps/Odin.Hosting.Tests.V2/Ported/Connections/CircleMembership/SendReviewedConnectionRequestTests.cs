#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Connections;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// <c>POST connections/requests/send-reviewed</c>, and the accept it mirrors: a connection request sent or
/// accepted by the owner -- from the console or from an app -- is the connection review happening at
/// request time, so its circles are routed the way <c>POST review</c> routes them and the connection comes
/// out reviewed and confirmed.
/// </summary>
[TestFixture]
public class SendReviewedConnectionRequestTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task AppSentReviewedRequest_IsReviewedAndConfirmedWhenItCompletes()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var (app, _) = await SetupAppWithReadCircleAsync(frodo);

        var response = await SendReviewedAsync(app, sam.Identity, []);
        AssertConnected(response);

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.ReviewedAt, Is.Not.Null, "an app sending as the owner is a review");
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(SystemCircleConstants.ConfirmedConnectionsCircleId), Is.True,
            "a reviewed connection is a confirmed one");
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(SystemCircleConstants.AutoConnectionsCircleId), Is.False,
            "and not an auto-connection");
    }

    [Test]
    public async Task AppAutoConnect_IsUnchanged_AndStaysNew()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var (app, _) = await SetupAppWithReadCircleAsync(frodo);

        var response = await new V2ConnectionRequestsClient(app.Identity, app.Factory).AutoConnectAsync(Header(sam.Identity, []));
        AssertConnected(response);

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.ReviewedAt, Is.Null, "the older endpoint keeps its behaviour");
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(SystemCircleConstants.AutoConnectionsCircleId), Is.True);
    }

    [Test]
    public async Task AppSentReviewedRequest_GrantsItsOwnReadCircleWithTheStorageKey()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var (app, circle) = await SetupAppWithReadCircleAsync(frodo);

        AssertConnected(await SendReviewedAsync(app, sam.Identity, [circle]));

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.PeerKeyStore.CircleGrants.TryGetValue(circle, out var grant), Is.True,
            "a circle the app can read is granted outright");
        Assert.That(grant!.KeyStoreKeyEncryptedDriveGrants.All(dg => dg.KeyStoreKeyEncryptedStorageKey != null), Is.True,
            "with the drive's storage key, not keyless");
    }

    [Test]
    public async Task AppSentReviewedRequest_QueuesAnotherAppsCircle()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var (app, _) = await SetupAppWithReadCircleAsync(frodo);
        var otherAppsCircle = await CreateOtherAppsReadCircleAsync(frodo);

        AssertConnected(await SendReviewedAsync(app, sam.Identity, [otherAppsCircle]));

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(otherAppsCircle), Is.False,
            "a circle the app cannot read must not be minted keyless");
        var queued = icr.PeerKeyStore.PendingEnrollments.SingleOrDefault(p => p.CircleId == otherAppsCircle);
        Assert.That(queued, Is.Not.Null, "it is queued for the app that owns it, as a review would");
        Assert.That(queued!.RequestedByAppId, Is.EqualTo(app.AppId));
        Assert.That(icr.ReviewedAt, Is.Not.Null);
    }

    [Test]
    public async Task AppSentReviewedRequest_NamingAnOwnerCircle_IsRefusedAndSendsNothing()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var (app, _) = await SetupAppWithReadCircleAsync(frodo);

        var ownerDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(ownerDrive, "ownerDrive", allowAnonymousReads: false);
        var ownerCircle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(ownerCircle, "owner-owned", WriteGrant(ownerDrive));

        var response = await SendReviewedAsync(app, sam.Identity, [ownerCircle]);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"got {response.StatusCode}");

        var sent = await frodo.Connections.GetOutgoingSentRequestTo(sam.Identity);
        Assert.That(sent.Content, Is.Null, "a refused circle must leave no request behind");
    }

    [Test]
    public async Task OwnerSentReviewedRequest_IsReviewedWhenItCompletes()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var response = await new V2ConnectionRequestsClient(frodo.Identity, frodo.Factory)
            .SendReviewedAsync(Header(sam.Identity, []));
        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");

        // An owner request waits for a manual accept.
        await sam.Connections.AcceptConnectionRequest(frodo.Identity);

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.ReviewedAt, Is.Not.Null);
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(SystemCircleConstants.ConfirmedConnectionsCircleId), Is.True);
    }

    [Test]
    public async Task AppAccept_QueuesAnotherAppsCircle_AsAReviewWould()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var (app, _) = await SetupAppWithReadCircleAsync(frodo);
        var otherAppsCircle = await CreateOtherAppsReadCircleAsync(frodo);

        Assert.That((await sam.Connections.SendConnectionRequest(frodo.Identity)).IsSuccessStatusCode, Is.True);

        var accept = await new V2ConnectionRequestsClient(app.Identity, app.Factory)
            .AcceptIncomingRequestAsync(sam.Identity, new AcceptConnectionRequestV2 { CircleIds = [otherAppsCircle] });
        Assert.That(accept.IsSuccessStatusCode, Is.True, $"got {accept.StatusCode}");

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.ReviewedAt, Is.Not.Null);
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(otherAppsCircle), Is.False);
        Assert.That(icr.PeerKeyStore.PendingEnrollments.Any(p => p.CircleId == otherAppsCircle), Is.True);
    }

    private static ConnectionRequestHeader Header(OdinId recipient, List<GuidId> circleIds) => new()
    {
        Recipient = recipient,
        Message = "send-reviewed",
        ContactData = new ContactRequestData { Name = "test" },
        CircleIds = circleIds
    };

    private static Task<Refit.ApiResponse<ConnectionRequestResult>> SendReviewedAsync(
        AppSession app, OdinId recipient, List<GuidId> circleIds) =>
        new V2ConnectionRequestsClient(app.Identity, app.Factory).SendReviewedAsync(Header(recipient, circleIds));

    private static void AssertConnected(Refit.ApiResponse<ConnectionRequestResult> response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"got {response.StatusCode}");
        Assert.That(response.Content!.Outcome, Is.EqualTo(AutoConnectOutcome.Connected), response.Content.Detail);
    }

    private async Task<IdentityConnectionRegistration> GetIcrAsync(OwnerSession owner, OdinId other)
    {
        var icr = await Host.GetTenantScope(owner.Identity.DomainName).Resolve<CircleNetworkStorage>().GetAsync(other);
        Assert.That(icr, Is.Not.Null);
        return icr!;
    }

    private static PermissionSetGrantRequest WriteGrant(TargetDrive drive) => new()
    {
        Drives = [new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Write } }],
        PermissionSet = new PermissionSet(new List<int>())
    };

    /// <summary>An app that can read one drive, owning a read circle on it.</summary>
    private static async Task<(AppSession app, Guid circle)> SetupAppWithReadCircleAsync(OwnerSession frodo)
    {
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "readDrive", allowAnonymousReads: false);

        var appId = Guid.NewGuid();
        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.Read, PermissionKeyAllowance.Apps, knownAppId: appId);

        var circle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circle, "read-circle", new PermissionSetGrantRequest
        {
            Drives = [new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Read } }],
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: appId);

        return (app, circle);
    }

    /// <summary>A read circle owned by some other app, on a drive the app under test has nothing on.</summary>
    private static async Task<Guid> CreateOtherAppsReadCircleAsync(OwnerSession frodo)
    {
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "otherDrive", allowAnonymousReads: false);

        var circle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circle, "other-apps", new PermissionSetGrantRequest
        {
            Drives = [new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Read } }],
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: Guid.NewGuid());

        return circle;
    }
}
