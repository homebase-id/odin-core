using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests._Universal.Owner.Connections;

/// <summary>
/// The connections list a peer may see is a list of identities, never a list of the owner's judgments.
/// docs/connection-defaults.md, "Viewer-scoped redaction".
/// </summary>
public class ConnectionListRedactionTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities:
            new List<TestIdentity> { TestIdentities.Frodo, TestIdentities.Merry, TestIdentities.Samwise });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }

    [Test]
    public async Task AGuestSeesIdentitiesWithoutTheOwnersJudgments()
    {
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        await sam.Connections.DisconnectFrom(frodo.Identity.OdinId);
        await frodo.Connections.DisconnectFrom(sam.Identity.OdinId);

        await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        await sam.Connections.AcceptConnectionRequest(frodo.OdinId);

        // Control: Sam's own client sees the whole record, review included.
        var ownerView = await sam.Network.GetConnectionInfo(frodo.OdinId);
        ClassicAssert.IsTrue(ownerView.IsSuccessStatusCode);
        ClassicAssert.IsNotNull(ownerView.Content.ReviewedAt);
        ClassicAssert.IsNotNull(ownerView.Content.AccessGrant);

        // Merry logs in to Sam's identity over YouAuth, holding nothing but ReadConnections.
        var guest = new ConnectedIdentityLoggedInOnGuestApi(TestIdentities.Merry.OdinId,
            new TestPermissionKeyList(PermissionKeys.ReadConnections));
        await guest.Initialize(sam);

        try
        {
            // The factory's identity is the host being called -- Sam -- not the guest doing the calling.
            var client = guest.GetFactory().CreateHttpClient(TestIdentities.Samwise.OdinId, out var sharedSecret);
            var svc = RefitCreator.RestServiceFor<IRefitGuestCircleNetworkConnections>(client, sharedSecret);

            var guestView = await svc.GetConnectedIdentities(100, null);
            ClassicAssert.IsTrue(guestView.IsSuccessStatusCode, $"guest list failed: {guestView.StatusCode}");

            var frodosEntry = guestView.Content.Results.SingleOrDefault(r => r.OdinId == frodo.OdinId);
            ClassicAssert.IsNotNull(frodosEntry, "the guest must still see who Sam is connected to");

            // ...and nothing about what Sam thinks of them, or how they came to be connected.
            ClassicAssert.IsNull(frodosEntry.ReviewedAt, "the review must never reach a third party");
            ClassicAssert.IsFalse(frodosEntry.Vetted);
            ClassicAssert.IsNull(frodosEntry.AccessGrant, "grants and their circles must never reach a third party");
            ClassicAssert.IsNull(frodosEntry.IntroducerOdinId);
            ClassicAssert.AreEqual(ConnectionRequestOrigin.None, frodosEntry.ConnectionRequestOrigin);
            ClassicAssert.IsFalse(frodosEntry.HasVerificationHash);
        }
        finally
        {
            await guest.Cleanup();
        }

        await sam.Connections.DisconnectFrom(frodo.Identity.OdinId);
        await frodo.Connections.DisconnectFrom(sam.Identity.OdinId);
    }
}
