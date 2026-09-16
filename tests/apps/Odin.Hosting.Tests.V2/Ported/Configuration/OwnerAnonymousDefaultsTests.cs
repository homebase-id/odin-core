using System;
using System.Linq;
using System.Net;
using System.Net.Http;
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

        var frodoInitResponse = await frodo.RefitFor<IRefitOwnerConfiguration>().InitializeIdentity(new InitialSetupRequest
        {
            Drives = null,
            Circles = null
        });

        Assert.That(frodoInitResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(frodoInitResponse.Content, Is.True);

        var samInitResponse = await sam.RefitFor<IRefitOwnerConfiguration>().InitializeIdentity(new InitialSetupRequest
        {
            Drives = null,
            Circles = null
        });

        Assert.That(samInitResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(samInitResponse.Content, Is.True);

        await ConnectAsync(frodo, sam);

        {
            var getConnectionsResponse = await AnonymousConnectionsOf(Identities.Sam);
            Assert.That(getConnectionsResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
                "Should have failed to get connections with 403 status code.");
        }

        await sam.Admin.UpdateTenantSettingsFlag(
            TenantConfigFlagNames.AnonymousVisitorsCanViewConnections, bool.TrueString);

        {
            var getConnectionsResponse = await AnonymousConnectionsOf(Identities.Sam);
            Assert.That(getConnectionsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(getConnectionsResponse.Content, Is.Not.Null);
            Assert.That(getConnectionsResponse.Content!.Results, Is.Not.Empty);
        }

        await DisconnectAsync(frodo, sam);
    }

    [Test]
    public async Task SystemDefault_AnonymousVisitorsCannotViewConnections()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var senderInitResponse = await sender.RefitFor<IRefitOwnerConfiguration>().InitializeIdentity(new InitialSetupRequest
        {
            Drives = null,
            Circles = null
        });

        Assert.That(senderInitResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(senderInitResponse.Content, Is.True);

        var recipientInitResponse = await recipient.RefitFor<IRefitOwnerConfiguration>().InitializeIdentity(new InitialSetupRequest
        {
            Drives = null,
            Circles = null
        });

        Assert.That(recipientInitResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(recipientInitResponse.Content, Is.True);

        await ConnectAsync(sender, recipient);

        // The sender's own connection list, with the flag left at its default.
        var getConnectionsResponse = await AnonymousConnectionsOf(Identities.Frodo);
        Assert.That(getConnectionsResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            "Should have failed to get connections with 403 status code.");

        await DisconnectAsync(sender, recipient);
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
    /// <see cref="V2Fixture.Host"/>'s raw client, which carries no token or shared secret.
    /// </summary>
    private async Task<ApiResponse<Odin.Core.PagedResult<RedactedIdentityConnectionRegistration>>>
        AnonymousConnectionsOf(string identity)
    {
        using var client = new HttpClient(Host.Server.CreateHandler())
        {
            BaseAddress = new Uri($"https://{identity}/")
        };

        var svc = RestService.For<ICircleNetworkYouAuthClient>(client);
        return await svc.GetConnectedProfiles(1, "");
    }
}
