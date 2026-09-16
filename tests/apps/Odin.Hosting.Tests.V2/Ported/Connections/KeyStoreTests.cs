using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Port of <c>OwnerApi/Membership/Connections/KeyStoreTests</c>. Whether a circle's drive grant
/// carries a storage key on the connected identity's ICR: it does when the circle grants
/// <see cref="DrivePermission.Read"/>, and does not when it doesn't.
/// </summary>
/// <remarks>
/// Carried defect: the no-read case builds its <c>PermissionedDrive</c> with
/// <c>DrivePermission.Write &amp; DrivePermission.WriteReactionsAndComments</c> — a bitwise AND of
/// 2 and 12, i.e. <see cref="DrivePermission.None"/>, where <c>|</c> was plainly meant. The test
/// still demonstrates what its name says (no <c>Read</c> ⇒ no storage key), just with a weaker
/// grant than intended. Left exactly as written — fixing it inside a port would make the diff
/// unreviewable.
/// <para>
/// The original created its circle through <c>client.Membership.CreateCircle</c>, which generates
/// the circle id itself and hands back the definition; here the id is generated test-side and
/// passed to <c>owner.Admin.CreateCircle</c>. The only thing either test reads off the circle is
/// that id. <c>owner.Admin.CreateCircle</c> also spells the description as
/// <c>"Description for {name}"</c>, which no assertion reads.
/// </para>
/// <para>
/// Both tests ended on <c>DisconnectIdentities</c>, pure lifecycle cleanup that per-test reset now
/// covers; dropped. <c>SetupCallerWithOwner</c> ordering is not in play — this fixture has no
/// caller matrix and uses <c>LoginAsOwner</c> only.
/// </para>
/// </remarks>
[TestFixture]
public class KeyStoreTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task ExchangeGrantHasNoStorageKey_WhenDrivePermissionRead_IsNot_Granted()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var senderChatDrive = TargetDrive.NewTargetDrive();
        await sender.Admin.CreateDrive(senderChatDrive, "Chat drive",
            allowAnonymousReads: false,
            ownerOnly: false,
            allowSubscriptions: false);

        var expectedPermissionedDrive = new PermissionedDrive()
        {
            Drive = senderChatDrive,
            Permission = DrivePermission.Write & DrivePermission.WriteReactionsAndComments
        };

        var senderChatCircleId = Guid.NewGuid();
        await sender.Admin.CreateCircle(senderChatCircleId, "Chat Participants", new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = expectedPermissionedDrive
                }
            }
        });

        var send = await sender.Connections.SendConnectionRequest(recipient.Identity, new List<GuidId>() { senderChatCircleId });
        Assert.That(send.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var accept = await recipient.Connections.AcceptConnectionRequest(sender.Identity, new List<GuidId>());
        Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Test
        // At this point: recipient should have an ICR record on sender's identity that does not have a key
        //

        var recipientConnectionInfo = await sender.Connections.GetConnectionInfo(recipient.Identity);

        //find the drive grant
        var actualCircleGrant = recipientConnectionInfo.Content!.AccessGrant.CircleGrants.SingleOrDefault(cg =>
            cg.DriveGrants.Any(dg => dg.PermissionedDrive == expectedPermissionedDrive));
        Assert.That(actualCircleGrant, Is.Not.Null);
        Assert.That(actualCircleGrant!.DriveGrants.Count, Is.EqualTo(1),
            "There should only be drive grant from the single circle we created");
        Assert.That(actualCircleGrant.DriveGrants.Single().HasStorageKey, Is.False,
            "the drive granted should not have a storage key");
    }

    [Test]
    public async Task ExchangeGrantIsGivenStorageKey_WhenDrivePermissionRead_IS_Granted()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var senderChatDrive = TargetDrive.NewTargetDrive();
        await sender.Admin.CreateDrive(senderChatDrive, "Chat drive",
            allowAnonymousReads: false,
            ownerOnly: false,
            allowSubscriptions: false);

        var expectedPermissionedDrive = new PermissionedDrive()
        {
            Drive = senderChatDrive,
            Permission = DrivePermission.Read | DrivePermission.Write | DrivePermission.WriteReactionsAndComments
        };

        var senderChatCircleId = Guid.NewGuid();
        await sender.Admin.CreateCircle(senderChatCircleId, "Chat Participants", new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = expectedPermissionedDrive
                }
            }
        });

        var send = await sender.Connections.SendConnectionRequest(recipient.Identity, new List<GuidId>() { senderChatCircleId });
        Assert.That(send.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var accept = await recipient.Connections.AcceptConnectionRequest(sender.Identity, new List<GuidId>());
        Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Test
        // At this point: recipient should have an ICR record on sender's identity that does not have a key
        //

        var recipientConnectionInfo = await sender.Connections.GetConnectionInfo(recipient.Identity);

        //find the drive grant
        var actualCircleGrant = recipientConnectionInfo.Content!.AccessGrant.CircleGrants.SingleOrDefault(cg =>
            cg.DriveGrants.Any(dg => dg.PermissionedDrive == expectedPermissionedDrive));
        Assert.That(actualCircleGrant, Is.Not.Null);
        Assert.That(actualCircleGrant!.DriveGrants.Count, Is.EqualTo(1),
            "There should only be drive grant from the single circle we created");
        Assert.That(actualCircleGrant.DriveGrants.Single().HasStorageKey, Is.True,
            "the drive granted should have storage key");
    }
}
