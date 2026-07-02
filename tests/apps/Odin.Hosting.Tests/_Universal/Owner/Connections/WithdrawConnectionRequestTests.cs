using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Odin.Hosting.Tests._Universal.Owner.Connections;

public class WithdrawConnectionRequestTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo, TestIdentities.Samwise });
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
    public async Task CancelingASentRequestWithNotifyRemoteWithdrawsThePendingRequestOnRecipient()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var sendResponse = await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        ClassicAssert.IsTrue(sendResponse.IsSuccessStatusCode);

        // Sanity: Sam has the incoming/pending request
        var samPendingBefore = await sam.Connections.GetIncomingRequestFrom(frodo.OdinId);
        ClassicAssert.IsTrue(samPendingBefore.IsSuccessStatusCode);
        ClassicAssert.IsNotNull(samPendingBefore.Content);

        // Frodo cancels the sent request, opting in to notify the remote so Sam withdraws the pending request too
        var deleteResponse = await frodo.Connections.DeleteSentRequestTo(sam.OdinId, notifyRemote: true);
        ClassicAssert.IsTrue(deleteResponse.IsSuccessStatusCode);

        // Frodo's sent request is gone immediately
        var frodoSent = await frodo.Connections.GetOutgoingSentRequestTo(sam.OdinId);
        ClassicAssert.IsTrue(frodoSent.Content == null || frodoSent.StatusCode == HttpStatusCode.NotFound);

        // The withdrawal notification is delivered to Sam via Frodo's outbox.
        await frodo.Connections.AwaitIntroductionsProcessing(TimeSpan.FromSeconds(40));

        // Sam's pending request should now be withdrawn
        var samPendingAfter = await sam.Connections.GetIncomingRequestFrom(frodo.OdinId);
        ClassicAssert.IsTrue(samPendingAfter.Content == null || samPendingAfter.StatusCode == HttpStatusCode.NotFound,
            $"Sam still had a pending request from Frodo after Frodo cancelled it (status: {samPendingAfter.StatusCode})");
    }

    [Test]
    public async Task CancelingASentRequestIsOneSidedByDefault()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var sendResponse = await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        ClassicAssert.IsTrue(sendResponse.IsSuccessStatusCode);

        // Default cancel (notifyRemote omitted) must NOT notify the recipient -- this preserves the
        // intentional asymmetric behavior the connection flow relies on.
        var deleteResponse = await frodo.Connections.DeleteSentRequestTo(sam.OdinId);
        ClassicAssert.IsTrue(deleteResponse.IsSuccessStatusCode);

        // Frodo's sent request is gone...
        var frodoSent = await frodo.Connections.GetOutgoingSentRequestTo(sam.OdinId);
        ClassicAssert.IsTrue(frodoSent.Content == null || frodoSent.StatusCode == HttpStatusCode.NotFound);

        // ...but Sam still holds the pending request (no withdrawal notification sent).
        var samPending = await sam.Connections.GetIncomingRequestFrom(frodo.OdinId);
        ClassicAssert.IsTrue(samPending.IsSuccessStatusCode);
        ClassicAssert.IsNotNull(samPending.Content,
            $"Sam should still have a pending request from Frodo after a one-sided cancel (status: {samPending.StatusCode})");

        // cleanup -- withdraw Sam's stranded pending request so it doesn't leak into other tests
        await sam.Connections.DeleteConnectionRequestFrom(frodo.OdinId);
    }
}
