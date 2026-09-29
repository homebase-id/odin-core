#nullable enable
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Membership.Connections.Requests;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Port of <c>_Universal/Owner/Connections/VerifyConnectionTests</c>. What
/// <c>POST /circles/connections/verify-connection</c> reports for each half-broken ICR, and that a
/// connection request may be re-sent whenever one of the two sides has dropped its record.
/// </summary>
/// <remarks>
/// <para>
/// Carried defects, behaviour left exactly as found:
/// <list type="bullet">
/// <item><see cref="VerifyConnectionFailsWhenRecipientNotConnected"/> and
/// <see cref="VerificationReturnsFalseWhenSenderNotConnectedButRecipientIs"/> are the same test: both
/// have <b>Frodo</b> (the verifier) drop his side and then assert
/// <c>IsValid == false, RemoteIdentityWasConnected == null</c>. The first is also misnamed — the
/// recipient never disconnects in it.</item>
/// <item><see cref="WillVerifyValidConnectionWhenAlreadyConnectedAndReturnBadRequest"/> never calls
/// <c>VerifyConnection</c> despite its name; it re-sends a connection request over a live connection
/// and asserts 400.</item>
/// <item>The fixture booted Merry solely so its trailing <c>Disconnect()</c> helper could disconnect
/// her from everyone. No test ever used her otherwise; she is not booted here.</item>
/// </list>
/// </para>
/// <para>
/// Every disconnect in the original went out with <c>notifyRemote: false</c> — the default on both
/// the <c>_Universal</c> requests client and the <c>_Universal</c> network client — which is what
/// makes the half-broken states the tests need. <c>owner.Connections.DisconnectFrom</c> sends the
/// same thing, so it is used directly; <c>VerifyConnection</c> is the system under test and is not
/// on <see cref="ConnectionsHandle"/>, so it goes through
/// <see cref="OwnerSession.RefitFor{T}"/>.
/// </para>
/// <para>
/// The trailing <c>Disconnect()</c> helper call in every test was lifecycle only and asserted
/// nothing; per-test reset owns that. <c>SetupCallerWithOwner</c> is not in play — no caller matrix,
/// <c>LoginAsOwner</c> only.
/// </para>
/// </remarks>
[TestFixture]
public class VerifyConnectionTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task VerificationReturnsFalseWhenRecipientNotConnectedButSenderIs()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await frodo.Connections.SendConnectionRequest(sam.Identity, []);
        await sam.Connections.AcceptConnectionRequest(frodo.Identity);

        await sam.Connections.DisconnectFrom(frodo.Identity);

        // resend it since we're already connected
        var response = await VerifyConnection(frodo, sam.Identity);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null);
        Assert.That(response.Content!.IsValid, Is.False);
        Assert.That(response.Content.RemoteIdentityWasConnected, Is.False);
    }

    [Test]
    public async Task VerificationReturnsFalseWhenSenderNotConnectedButRecipientIs()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await frodo.Connections.SendConnectionRequest(sam.Identity, []);
        await sam.Connections.AcceptConnectionRequest(frodo.Identity);

        await frodo.Connections.DisconnectFrom(sam.Identity);

        // resend it since we're already connected
        var response = await VerifyConnection(frodo, sam.Identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null);
        Assert.That(response.Content!.IsValid, Is.False);
        Assert.That(response.Content.RemoteIdentityWasConnected, Is.Null);
    }

    [Test]
    public async Task WillVerifyValidConnectionWhenAlreadyConnectedAndReturnBadRequest()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await frodo.Connections.SendConnectionRequest(sam.Identity, []);
        await sam.Connections.AcceptConnectionRequest(frodo.Identity);

        // resend it since we're already connected
        var response = await frodo.Connections.SendConnectionRequest(sam.Identity, []);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CanVerifyValidConnection()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await frodo.Connections.SendConnectionRequest(sam.Identity);
        await sam.Connections.AcceptConnectionRequest(frodo.Identity);

        var response = await VerifyConnection(frodo, sam.Identity);
        var result = response.Content;
        Assert.That(result, Is.Not.Null);
        Assert.That(response.IsSuccessStatusCode, Is.True);
        Assert.That(result!.IsValid, Is.True);
        Assert.That(result.RemoteIdentityWasConnected, Is.True);
    }

    [Test]
    public async Task VerifyConnectionFailsWhenRecipientNotConnected()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await frodo.Connections.SendConnectionRequest(sam.Identity, []);
        await sam.Connections.AcceptConnectionRequest(frodo.Identity);

        //
        // Now: have frodo delete it but sam keep it
        //
        await frodo.Connections.DisconnectFrom(sam.Identity);
        var response = await VerifyConnection(frodo, sam.Identity);
        Assert.That(response.IsSuccessStatusCode, Is.True);

        var result = response.Content;
        Assert.That(result!.IsValid, Is.False);
        Assert.That(result.RemoteIdentityWasConnected, Is.Null);
    }

    [Test]
    public async Task CanSendConnectionRequestWhenSenderNotConnectedButRecipientIsConnected()
    {
        // This causes an issue because the verification fails as it cannot get to the ICR key
        // this is because the sender is not connected to the recipient
        // but the recipient has a record of the sender

        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await frodo.Admin.DisableAutoAcceptIntroductions();
        await sam.Admin.DisableAutoAcceptIntroductions();

        await frodo.Connections.SendConnectionRequest(sam.Identity);
        await sam.Connections.AcceptConnectionRequest(frodo.Identity);

        await frodo.Connections.DisconnectFrom(sam.Identity);

        // resend it since we're already connected
        var response = await frodo.Connections.SendConnectionRequest(sam.Identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task CanSendingConnectionRequestWhenRecipientNotConnectedButSenderIsConnected()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await frodo.Connections.SendConnectionRequest(sam.Identity, []);
        await sam.Connections.AcceptConnectionRequest(frodo.Identity);

        await sam.Connections.DisconnectFrom(frodo.Identity);

        // resend it since we're already connected
        var response = await frodo.Connections.SendConnectionRequest(sam.Identity, []);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static Task<ApiResponse<IcrVerificationResult>> VerifyConnection(OwnerSession owner, OdinId recipient) =>
        owner.RefitFor<IRefitUniversalCircleNetworkConnections>()
            .VerifyConnection(new OdinIdRequest { OdinId = recipient });
}
