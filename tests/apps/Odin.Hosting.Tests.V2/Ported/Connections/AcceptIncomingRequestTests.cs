#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Connections;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Tests._V2.ApiClient;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Covers the V2 accept endpoint (PUT /api/v2/connections/requests/incoming/{senderId}) taking an
/// <see cref="AcceptConnectionRequestV2"/> body: circles named in the body are granted to the
/// sender on accept; the body is optional, so an empty object, null CircleIds, or no body at all
/// still accepts using the route sender.
/// </summary>
[TestFixture]
public class AcceptIncomingRequestTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task Accept_WithCircleIds_EstablishesConnection_AndGrantsCircles()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var circleId = Guid.NewGuid();
        await recipient.Admin.CreateCircle(circleId, "accept-test-circle", new PermissionSetGrantRequest
        {
            Drives = [],
            PermissionSet = new PermissionSet(PermissionKeys.ReadWhoIFollow)
        });

        var send = await sender.Connections.SendConnectionRequest(recipient.Identity);
        Assert.That(send.IsSuccessStatusCode, Is.True);

        var accept = await recipient.Connections.AcceptIncomingRequestV2Async(sender.Identity,
            new AcceptConnectionRequestV2
            {
                CircleIds = [new GuidId(circleId)]
            });
        Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), $"actual {accept.StatusCode}");

        await AssertBothSidesConnected(sender, recipient);

        var recipientIcr = await recipient.Connections.GetConnectionInfo(sender.Identity);
        var circleGrants = recipientIcr.Content!.AccessGrant.CircleGrants;
        Assert.That(circleGrants.Any(g => g.CircleId == circleId), Is.True,
            "the circle granted in the accept body should be on the sender's ICR");
    }

    [Test]
    public async Task Accept_WithNullCircleIds_Connects()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var send = await sender.Connections.SendConnectionRequest(recipient.Identity);
        Assert.That(send.IsSuccessStatusCode, Is.True);

        var accept = await recipient.Connections.AcceptIncomingRequestV2Async(sender.Identity,
            new AcceptConnectionRequestV2());
        Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), $"actual {accept.StatusCode}");

        await AssertBothSidesConnected(sender, recipient);
    }

    [Test]
    public async Task Accept_WithEmptyCircleIds_Connects()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var send = await sender.Connections.SendConnectionRequest(recipient.Identity);
        Assert.That(send.IsSuccessStatusCode, Is.True);

        var accept = await recipient.Connections.AcceptIncomingRequestV2Async(sender.Identity,
            new AcceptConnectionRequestV2 { CircleIds = [] });
        Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), $"actual {accept.StatusCode}");

        await AssertBothSidesConnected(sender, recipient);
    }

    [Test]
    public async Task Accept_WithNoBody_Connects()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var send = await sender.Connections.SendConnectionRequest(recipient.Identity);
        Assert.That(send.IsSuccessStatusCode, Is.True);

        var accept = await recipient.Connections.AcceptIncomingRequestV2Async(sender.Identity, null);
        Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), $"actual {accept.StatusCode}");

        await AssertBothSidesConnected(sender, recipient);
    }

    [Test]
    public async Task Accept_Forbidden_WhenAppLacksManageContacts()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var send = await sender.Connections.SendConnectionRequest(recipient.Identity);
        Assert.That(send.IsSuccessStatusCode, Is.True);

        // read keys alone must not be enough to accept
        var app = await BuildRecipientAppAsync(recipient,
            PermissionKeys.ReadConnectionRequests, PermissionKeys.ReadCircleMembership);
        var accept = await app.AcceptIncomingRequestAsync(sender.Identity, new AcceptConnectionRequestV2());

        Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"actual {accept.StatusCode}");

        // the pending request must be untouched
        var pending = await recipient.Connections.GetIncomingRequestFrom(sender.Identity);
        Assert.That(pending.IsSuccessStatusCode, Is.True);
        Assert.That(pending.Content, Is.Not.Null);
    }

    [Test]
    public async Task Accept_ByAppWithManageContactsAndTransit_Connects()
    {
        // The accept flow needs the caller's ICR key. Owner contexts always carry it; app grants
        // carry it only when the app is registered with a transit key (UseTransitRead/Write --
        // see AppRegistrationService.HasRequestedTransit). So an app needs ManageContacts for the
        // endpoint gate plus a transit key for the ICR key material.
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var send = await sender.Connections.SendConnectionRequest(recipient.Identity);
        Assert.That(send.IsSuccessStatusCode, Is.True);

        var app = await BuildRecipientAppAsync(recipient,
            PermissionKeys.ManageContacts, PermissionKeys.ReadConnectionRequests,
            PermissionKeys.ReadCircleMembership, PermissionKeys.UseTransitWrite);
        var accept = await app.AcceptIncomingRequestAsync(sender.Identity, new AcceptConnectionRequestV2());

        Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.NoContent),
            $"actual {accept.StatusCode}; body: {accept.Error?.Content}");

        await AssertBothSidesConnected(sender, recipient);
    }

    private static async Task<V2ConnectionRequestsClient> BuildRecipientAppAsync(OwnerSession recipient, params int[] permissionKeys)
    {
        // The app needs a drive grant to register against; the drive itself is irrelevant here.
        var drive = TargetDrive.NewTargetDrive();
        await recipient.Admin.CreateDrive(drive, "accept-perm-test-drive", allowAnonymousReads: false);
        var app = await AppSession.SetupAsync(recipient, drive, DrivePermission.None, permissionKeys);
        return new V2ConnectionRequestsClient(app.Identity, app.Factory);
    }

    private static async Task AssertBothSidesConnected(OwnerSession sender, OwnerSession recipient)
    {
        var senderIcr = await sender.Connections.GetConnectionInfo(recipient.Identity);
        Assert.That(senderIcr.IsSuccessStatusCode, Is.True);
        Assert.That(senderIcr.Content!.Status, Is.EqualTo(ConnectionStatus.Connected));

        var recipientIcr = await recipient.Connections.GetConnectionInfo(sender.Identity);
        Assert.That(recipientIcr.IsSuccessStatusCode, Is.True);
        Assert.That(recipientIcr.Content!.Status, Is.EqualTo(ConnectionStatus.Connected));
    }
}
