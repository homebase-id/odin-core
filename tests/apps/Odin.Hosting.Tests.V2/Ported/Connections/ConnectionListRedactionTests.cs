#nullable enable
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Util;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Port of <c>_Universal/Owner/Connections/ConnectionListRedactionTests</c>. The connections list a
/// peer may see is a list of identities, never a list of the owner's judgments.
/// docs/connection-defaults.md, "Viewer-scoped redaction".
/// </summary>
/// <remarks>
/// <para>
/// <c>ConnectedIdentityLoggedInOnGuestApi</c> does not connect anything despite its name — it
/// creates a circle carrying the permission keys, registers the named domain as a YouAuth domain
/// granted that circle, and registers a client under it. That is exactly
/// <see cref="GuestSession.SetupAsync(OwnerSession, PermissionSetGrantRequest, AsciiDomainName?)"/>,
/// whose <c>domain</c> parameter supplies the one thing this test needs beyond the common case: the
/// viewer has to be Merry by name, not a throwaway domain. The only difference is the YouAuth
/// client's friendly name, which nothing reads.
/// </para>
/// <para>
/// Merry is therefore not in <c>HostIdentities</c>: she is only ever a domain name on a registration,
/// never an identity the server resolves, so booting her tenant would cost a materialisation and a
/// reset per test for nothing.
/// </para>
/// <para>
/// The leading disconnects and the trailing <c>guest.Cleanup()</c> / disconnect pair were state
/// hygiene for a shared <c>WebScaffold</c>; per-test reset owns that now, and none of them asserted.
/// <c>SetupCallerWithOwner</c> is not in play — no caller matrix, <c>LoginAsOwner</c> only.
/// </para>
/// </remarks>
[TestFixture]
public class ConnectionListRedactionTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Sam, Identities.Frodo];

    [Test]
    public async Task AGuestSeesIdentitiesWithoutTheOwnersJudgments()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var frodo = await LoginAsOwner(Identities.Frodo);

        await frodo.Connections.SendConnectionRequest(sam.Identity, []);
        await sam.Connections.AcceptConnectionRequest(frodo.Identity);

        // Control: Sam's own client sees the whole record, review included.
        var ownerView = await sam.Connections.GetConnectionInfo(frodo.Identity);
        Assert.That(ownerView.IsSuccessStatusCode, Is.True);
        Assert.That(ownerView.Content!.ReviewedAt, Is.Not.Null);
        Assert.That(ownerView.Content.AccessGrant, Is.Not.Null);

        // Merry logs in to Sam's identity over YouAuth, holding nothing but ReadConnections.
        var guest = await GuestSession.SetupAsync(sam, new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        }, new AsciiDomainName(Identities.Merry));

        // The guest session's identity is the host being called -- Sam -- not the guest doing the calling.
        var guestView = await guest.RefitFor<IRefitGuestCircleNetworkConnections>()
            .GetConnectedIdentities(100, null);
        Assert.That(guestView.IsSuccessStatusCode, Is.True);

        var frodosEntry = guestView.Content!.Results.SingleOrDefault(r => r.OdinId == frodo.Identity);
        Assert.That(frodosEntry, Is.Not.Null, "the guest must still see who Sam is connected to");

        // ...and nothing about what Sam thinks of them, or how they came to be connected.
        Assert.That(frodosEntry!.ReviewedAt, Is.Null, "the review must never reach a third party");
        Assert.That(frodosEntry.AccessGrant, Is.Null, "grants and their circles must never reach a third party");
        Assert.That(frodosEntry.IntroducerOdinId, Is.Null);
        Assert.That(frodosEntry.ConnectionRequestOrigin, Is.EqualTo(ConnectionRequestOrigin.None));
        Assert.That(frodosEntry.HasVerificationHash, Is.False);
    }
}
