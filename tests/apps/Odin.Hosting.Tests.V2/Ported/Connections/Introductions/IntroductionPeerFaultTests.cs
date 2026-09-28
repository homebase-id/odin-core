#nullable enable
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using static Odin.Hosting.Tests.V2.Ported.Connections.Introductions.IntroductionTestUtils;

namespace Odin.Hosting.Tests.V2.Ported.Connections.Introductions;

/// <summary>
/// An introduction survives one failed peer call (#1778).
/// </summary>
/// <remarks>
/// Accepting a connection request writes the accepting side's connection first and then calls back to
/// the requester (<c>establishconnection</c>) so it can write its own. These tests fail that callback
/// once, the way a transient fault under load does, and assert that both sides still end up holding a
/// working connection -- not one side Connected and the other holding nothing.
/// </remarks>
[TestFixture]
public class IntroductionPeerFaultTests : V2Fixture
{
    private const string EstablishConnectionPath = "/establishconnection";

    protected override IReadOnlyCollection<string> ToleratedErrorLogSubstrings =>
    [
        // The behaviour under test: the injected failure of the establishconnection callback, seen by an
        // auto-accept and by the owner's accept endpoint respectively.
        "Failed while trying to auto-accept a connection request",
        "Failed to establish connection request",
        OutboxDeliveryFailureLogged,
    ];

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Merry, Identities.Sam];

    /// <summary>A fault a test armed but never triggered must not fire in the next test.</summary>
    [TearDown]
    public void ClearPeerFaults() => Host.PeerFaults.Clear();

    /// <summary>
    /// Sam's introductory request reaches Merry, who accepts it on arrival; her callback to Sam fails.
    /// </summary>
    [Test]
    public async Task IntroduceesConnectWhenTheAutoAcceptCallbackFailsOnce()
    {
        var (frodo, sam, merry) = await PrepareAsync();

        Host.PeerFaults.FailNext(from: merry.Identity, to: sam.Identity, EstablishConnectionPath);

        await IntroduceAndDrainAsync(frodo, sam, merry);

        await AssertConnectedBothWaysAsync(sam, merry);
    }

    /// <summary>
    /// Sam and Merry are connected by the first request; Merry's own introductory request then reaches
    /// Sam, who accepts it again, and his callback to Merry fails.
    /// </summary>
    [Test]
    public async Task IntroduceesStayConnectedWhenTheSecondCallbackFailsOnce()
    {
        var (frodo, sam, merry) = await PrepareAsync();

        Host.PeerFaults.FailNext(from: sam.Identity, to: merry.Identity, EstablishConnectionPath);

        await IntroduceAndDrainAsync(frodo, sam, merry);

        await AssertConnectedBothWaysAsync(sam, merry);
    }

    /// <summary>
    /// The same fault on an owner's own accept: the accept fails, so the accepting side must not be left
    /// claiming a connection the requester never recorded -- and accepting again must work.
    /// </summary>
    [Test]
    public async Task AFailedAcceptLeavesNoOneSidedConnection()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        var send = await sam.Connections.SendConnectionRequest(merry.Identity, []);
        Assert.That(send.IsSuccessStatusCode, Is.True, $"send failed: {send.StatusCode}");

        Host.PeerFaults.FailNext(from: merry.Identity, to: sam.Identity, EstablishConnectionPath);

        var failedAccept = await merry.Connections.AcceptConnectionRequest(sam.Identity);
        Assert.That(failedAccept.IsSuccessStatusCode, Is.False, "the accept cannot succeed while its callback fails");

        Assert.That(await StatusAsync(merry, sam.Identity), Is.Not.EqualTo(ConnectionStatus.Connected),
            $"{merry.Identity} must not hold a connection {sam.Identity} never recorded");
        Assert.That(await StatusAsync(sam, merry.Identity), Is.Not.EqualTo(ConnectionStatus.Connected));

        var incoming = await merry.Connections.GetIncomingRequestFrom(sam.Identity);
        Assert.That(incoming.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "the request must still be there to accept again");

        var accept = await merry.Connections.AcceptConnectionRequest(sam.Identity);
        Assert.That(accept.IsSuccessStatusCode, Is.True, $"retrying the accept failed: {accept.StatusCode}");

        await AssertConnectedBothWaysAsync(sam, merry);
    }

    private async Task<(OwnerSession frodo, OwnerSession sam, OwnerSession merry)> PrepareAsync()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);
        await PrepareIntroducer(frodo, sam, merry);
        return (frodo, sam, merry);
    }

    private static async Task IntroduceAndDrainAsync(OwnerSession frodo, OwnerSession sam, OwnerSession merry)
    {
        var response = await Requests(frodo).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.DrainOutboxAsync();
        await merry.Sync.DrainOutboxAsync();
    }

    private static async Task<ConnectionStatus> StatusAsync(OwnerSession owner, OdinId other)
    {
        var info = await owner.Connections.GetConnectionInfo(other);
        Assert.That(info.IsSuccessStatusCode, Is.True);
        return info.Content!.Status;
    }

    /// <summary>
    /// Connected on both sides, and each side's credentials are the ones the other side issued --
    /// a status of Connected alone does not show the two records belong to the same handshake.
    /// </summary>
    private static async Task AssertConnectedBothWaysAsync(OwnerSession a, OwnerSession b)
    {
        Assert.That(await StatusAsync(a, b.Identity), Is.EqualTo(ConnectionStatus.Connected),
            $"{a.Identity} must hold {b.Identity} as a connection");
        Assert.That(await StatusAsync(b, a.Identity), Is.EqualTo(ConnectionStatus.Connected),
            $"{b.Identity} must hold {a.Identity} as a connection");

        foreach (var (from, to) in new[] { (a, b), (b, a) })
        {
            var verify = await new UniversalCircleNetworkApiClient(from.Identity, from.Factory).VerifyConnection(to.Identity);
            Assert.That(verify.IsSuccessStatusCode, Is.True, $"verify failed: {verify.StatusCode}");
            Assert.That(verify.Content!.IsValid, Is.True,
                $"{from.Identity}'s connection to {to.Identity} must verify from {from.Identity}'s side");
        }
    }
}
