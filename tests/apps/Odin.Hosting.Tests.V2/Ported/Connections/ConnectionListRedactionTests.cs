#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Util;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Services.Authentication.YouAuth;
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
/// granted that circle, and registers a client under it. <see cref="LoginAsGuestAsync"/> is that
/// same sequence against <see cref="OwnerAdmin"/>. <see cref="GuestSession"/> is not used because it
/// mints a random throwaway domain, and this test needs the viewer to be Merry by name.
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
        var guestFactory = await LoginAsGuestAsync(sam, new AsciiDomainName(Identities.Merry),
            PermissionKeys.ReadConnections);

        // The factory's identity is the host being called -- Sam -- not the guest doing the calling.
        var client = guestFactory.CreateHttpClient(sam.Identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IRefitGuestCircleNetworkConnections>(client, sharedSecret);

        var guestView = await svc.GetConnectedIdentities(100, null);
        Assert.That(guestView.IsSuccessStatusCode, Is.True, $"guest list failed: {guestView.StatusCode}");

        var frodosEntry = guestView.Content!.Results.SingleOrDefault(r => r.OdinId == frodo.Identity);
        Assert.That(frodosEntry, Is.Not.Null, "the guest must still see who Sam is connected to");

        // ...and nothing about what Sam thinks of them, or how they came to be connected.
        Assert.That(frodosEntry!.ReviewedAt, Is.Null, "the review must never reach a third party");
        Assert.That(frodosEntry.AccessGrant, Is.Null, "grants and their circles must never reach a third party");
        Assert.That(frodosEntry.IntroducerOdinId, Is.Null);
        Assert.That(frodosEntry.ConnectionRequestOrigin, Is.EqualTo(ConnectionRequestOrigin.None));
        Assert.That(frodosEntry.HasVerificationHash, Is.False);
    }

    /// <summary>
    /// Registers <paramref name="guestDomain"/> as a YouAuth domain on <paramref name="owner"/>,
    /// granted a circle carrying nothing but <paramref name="permissionKeys"/>, and hands back a
    /// factory authenticated as a client of that domain.
    /// </summary>
    private async Task<InProcessApiClientFactory> LoginAsGuestAsync(
        OwnerSession owner, AsciiDomainName guestDomain, params int[] permissionKeys)
    {
        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Circle with valid permissions",
            new PermissionSetGrantRequest
            {
                PermissionSet = new PermissionSet(permissionKeys)
            });

        await owner.Admin.RegisterYouAuthDomain(guestDomain, [circleId]);
        var clientReg = await owner.Admin.RegisterYouAuthClient(guestDomain, "test scenario client");

        var cat = ClientAccessToken.FromPortableBytes(clientReg.Content!.Data);
        return new InProcessApiClientFactory(Host, YouAuthDefaults.XTokenCookieName,
            cat.ToAuthenticationToken(), cat.SharedSecret, GuestApiPathConstantsV1.BasePathV1);
    }
}
