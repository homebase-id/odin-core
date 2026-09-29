using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.YouAuthApi.Circle;
using Odin.Services.Configuration;
using Odin.Services.Membership.Connections;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Configuration;

/// <summary>
/// Port of <c>OwnerApi/Configuration/SystemInit/OwnerAnonymousDefaultsTests</c>. An anonymous
/// visitor cannot see who an identity is connected to unless the owner has turned
/// <c>AnonymousVisitorsCanViewConnections</c> on.
/// </summary>
/// <remarks>
/// Deviations, all checked:
/// <list type="bullet">
/// <item>The original's <c>ConfigurationTestUtilities.CreateConnectionRequest</c> registered a sample
/// app on both sides purely to obtain <c>ContactData</c> for the request body; every call it then made
/// was as the owner. Here the request goes through <c>owner.Connections</c> with no contact card, so
/// no app is registered. Nothing in either test reads the contact data.</item>
/// <item>The original used Merry+Pippin for the second test only because a <c>WebScaffold</c> run
/// shares identities process-wide and the first test had already connected Frodo and Sam. Per-test
/// reset removes that constraint, so both tests use the same pair — but the second still queries the
/// <i>sender's</i> connection list and the first the <i>recipient's</i>, exactly as the originals did.</item>
/// <item>The trailing <c>DisconnectIdentities</c> is kept rather than dropped as lifecycle: it asserts
/// the post-disconnect status on both sides, and a call whose response is checked is a test.</item>
/// </list>
///
/// Both tests call <c>InitializeIdentity</c> and assert its response, so that call goes through
/// <see cref="Api.OwnerSession.RefitFor{T}"/>; the fixture baseline has already initialized both
/// tenants and the server call is idempotent. No caller matrix in the original and none added.
/// </remarks>
[TestFixture]
public class OwnerAnonymousDefaultsTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task CanAllowAnonymousToViewConnections()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await AssertInitializeIdentity(frodo);
        await AssertInitializeIdentity(sam);

        await ConnectAsync(frodo, sam);

        var before = await AnonymousConnectionsOf(Identities.Sam);
        Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        await sam.Admin.UpdateTenantSettingsFlag(
            TenantConfigFlagNames.AnonymousVisitorsCanViewConnections, bool.TrueString);

        var after = await AnonymousConnectionsOf(Identities.Sam);
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(after.Content, Is.Not.Null);
        Assert.That(after.Content!.Results, Is.Not.Empty);

        await DisconnectAsync(frodo, sam);
    }

    [Test]
    public async Task SystemDefault_AnonymousVisitorsCannotViewConnections()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        await AssertInitializeIdentity(sender);
        await AssertInitializeIdentity(recipient);

        await ConnectAsync(sender, recipient);

        // The sender's own connection list, with the flag left at its default.
        var getConnectionsResponse = await AnonymousConnectionsOf(Identities.Frodo);
        Assert.That(getConnectionsResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        await DisconnectAsync(sender, recipient);
    }

    /// <summary>
    /// Runs initial setup and asserts it succeeded. The fixture baseline has already initialized both
    /// tenants and the call is idempotent, but both originals asserted its response, so it is a test
    /// step rather than arrange — which is why it goes through <see cref="OwnerSession.RefitFor{T}"/>
    /// rather than <c>owner.Admin</c>.
    /// </summary>
    private static async Task AssertInitializeIdentity(OwnerSession owner)
    {
        var response = await owner.RefitFor<IRefitOwnerConfiguration>().InitializeIdentity(new InitialSetupRequest
        {
            Drives = null,
            Circles = null
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.True);
    }

    /// <summary>
    /// Send + accept, with the original's intermediate assertion that the request actually landed on
    /// the recipient before it is accepted.
    /// </summary>
    private static async Task ConnectAsync(OwnerSession sender, OwnerSession recipient)
    {
        var send = await sender.Connections.SendConnectionRequest(recipient.Identity);
        Assert.That(send.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var pending = await recipient.Connections.GetIncomingRequestFrom(sender.Identity);
        Assert.That(pending.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(pending.Content, Is.Not.Null, $"No request found from {sender.Identity}");
        Assert.That(pending.Content!.SenderOdinId, Is.EqualTo(sender.Identity.DomainName));

        var accept = await recipient.Connections.AcceptConnectionRequest(sender.Identity, Array.Empty<Odin.Core.GuidId>());
        Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await AssertConnectionStatus(recipient, sender, ConnectionStatus.Connected);
    }

    private static async Task DisconnectAsync(OwnerSession sender, OwnerSession recipient)
    {
        var senderDisconnect = await sender.Connections.DisconnectFrom(recipient.Identity);
        Assert.That(senderDisconnect.StatusCode, Is.EqualTo(HttpStatusCode.OK), "failed to disconnect");
        await AssertConnectionStatus(sender, recipient, ConnectionStatus.None);

        var recipientDisconnect = await recipient.Connections.DisconnectFrom(sender.Identity);
        Assert.That(recipientDisconnect.StatusCode, Is.EqualTo(HttpStatusCode.OK), "failed to disconnect");
        await AssertConnectionStatus(recipient, sender, ConnectionStatus.None);
    }

    private static async Task AssertConnectionStatus(OwnerSession owner, OwnerSession other, ConnectionStatus expected)
    {
        var response = await owner.Connections.GetConnectionInfo(other.Identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            $"Failed to get status for {other.Identity}");
        Assert.That(response.Content, Is.Not.Null, $"No status for {other.Identity} found");
        Assert.That(response.Content!.Status, Is.EqualTo(expected));
    }

    /// <summary>
    /// The YouAuth connected-profiles endpoint as a visitor holding nothing at all —
    /// <see cref="AnonymousHttp.AnonymousRefitFor{T}"/>, which carries no token or shared secret.
    /// </summary>
    private Task<ApiResponse<Odin.Core.PagedResult<RedactedIdentityConnectionRegistration>>>
        AnonymousConnectionsOf(string identity)
        => Host.AnonymousRefitFor<ICircleNetworkYouAuthClient>(identity).GetConnectedProfiles(1, "");
}
