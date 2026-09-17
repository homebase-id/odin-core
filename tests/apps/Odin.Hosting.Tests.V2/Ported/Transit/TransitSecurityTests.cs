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

namespace Odin.Hosting.Tests.V2.Ported.Transit;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/OwnerApi/Transit/Query/TransitSecurityTests.cs
///
/// The sender creates a drive and a circle granting access on it, sends a connection request that
/// includes that circle, and — once the recipient accepts — the sender's own connection record for
/// the recipient carries the circle's drive grant.
/// </summary>
/// <remarks>
/// The connection flow is hand-rolled rather than run through <see cref="Peer.PeerFlow"/>: the
/// original creates the drive on the <em>sender</em> only and grants in that one direction, whereas
/// <c>PeerFlow.ConnectAsync</c> always builds a recipient-side circle over a drive both identities
/// hold. The <c>CreateCircle</c> response is read back with <c>owner.Admin.GetCircleDefinition</c>,
/// since <c>owner.Admin.CreateCircle</c> hands back the raw response rather than the definition the
/// V1 client returned. The trailing <c>DisconnectIdentities</c> asserted nothing and is dropped —
/// per-test reset covers it.
/// <para>
/// <b>Carried defects — behaviour left exactly as found, and both are worth filing:</b>
/// <list type="bullet">
/// <item><description>
/// Both tests build the permissioned drive with
/// <c>DrivePermission.Write &amp; DrivePermission.WriteReactionsAndComments</c> — a bitwise
/// <c>AND</c>, almost certainly meant to be <c>|</c>. <c>Write</c> is <c>2</c> and
/// <c>WriteReactionsAndComments</c> is <c>React | Comment</c> = <c>12</c>, so the expression is
/// <c>0</c>, i.e. <see cref="DrivePermission.None"/>. The circle therefore grants no permission at
/// all, and the assertion — which compares the grant the server stored against the same
/// zero-permission drive — passes anyway. Neither test actually exercises the write access its name
/// and comments describe.
/// </description></item>
/// <item><description>
/// <see cref="WhenSenderOnlyHasWriteAccess_RecipientSendsToInbox"/> does not send anything to an
/// inbox. In the original, everything after the connection handshake — upload, outbox processing,
/// the recipient's inbox, and the transit read-back — was one ~320-line commented-out block, leaving
/// a body byte-identical to
/// <see cref="ExchangeGrantHasNoStorageKey_WhenDrivePermissionReadIsNotGranted"/>. The two tests are
/// therefore duplicates that both assert a circle grant exists. The commented-out block is not
/// carried here: it was written against <c>_scaffold.AppApi</c> / <c>OldOwnerApi</c>, which do not
/// exist in this framework, and is in git history on the deleted original if anyone revives it.
/// </description></item>
/// </list>
/// </para>
/// </remarks>
[TestFixture]
public class TransitSecurityTests : V2Fixture
{
    /// <remarks>
    /// Issue #1771. Under load the outbox retries a peer upload that trips the S2040 guard in
    /// <c>PeerFileWriter.GetTargetAcl</c> (a comment whose encryption disagrees with its referenced
    /// file); it is logged at Error four times — the drain's initial attempt plus its three retry
    /// passes — and the test's own assertions pass regardless. The V1 originals whitelisted the same
    /// message via <c>SetAssertLogEventsAction</c>, so this carries that over rather than conceding
    /// something new. Remove when #1771 is resolved.
    /// </remarks>
    protected override IReadOnlyCollection<string> ToleratedErrorLogSubstrings =>
        ["Referenced filed and metadata payload encryption do not match"];

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task ExchangeGrantHasNoStorageKey_WhenDrivePermissionReadIsNotGranted()
    {
        await ConnectWithSenderSideCircleAsync();
    }

    [Test]
    public async Task WhenSenderOnlyHasWriteAccess_RecipientSendsToInbox()
    {
        await ConnectWithSenderSideCircleAsync();
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The body both tests carry: sender makes a chat drive and a circle over it, sends a connection
    /// request granting that circle, recipient accepts granting nothing back, and the sender's
    /// connection record for the recipient is then checked for the circle's drive grant.
    /// </summary>
    private async Task ConnectWithSenderSideCircleAsync()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var senderChatDrive = TargetDrive.NewTargetDrive();
        await sender.Admin.CreateDrive(senderChatDrive, "Chat drive",
            allowAnonymousReads: false,
            ownerOnly: false,
            allowSubscriptions: false);

        var expectedPermissionedDrive = new PermissionedDrive
        {
            Drive = senderChatDrive,
            Permission = DrivePermission.Write & DrivePermission.WriteReactionsAndComments
        };

        var senderChatCircleId = Guid.NewGuid();
        await sender.Admin.CreateCircle(senderChatCircleId, "Chat Participants", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = expectedPermissionedDrive
                }
            }
        });

        var sendRequest = await sender.Connections.SendConnectionRequest(recipient.Identity, new List<GuidId> { senderChatCircleId });
        Assert.That(sendRequest.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var accept = await recipient.Connections.AcceptConnectionRequest(sender.Identity, new List<GuidId>());
        Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Test
        // At this point: recipient should have an ICR record on sender's identity that does not have a key
        //

        var senderChatCircle = await sender.Admin.GetCircleDefinition(senderChatCircleId);

        var recipientConnectionInfo = await sender.Connections.GetConnectionInfo(recipient.Identity);
        Assert.That(recipientConnectionInfo.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Assert.That(recipientConnectionInfo.Content!.AccessGrant.CircleGrants.SingleOrDefault(cg =>
            cg.DriveGrants.Any(dg => dg.PermissionedDrive == senderChatCircle.DriveGrants.Single().PermissionedDrive)), Is.Not.Null);
    }
}
