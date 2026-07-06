using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace Odin.Hosting.Tests._Universal.Owner.Connections;

public class ReciprocalDisconnectTests
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
    public async Task DisconnectNotifiesRemoteByDefault()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        await sam.Connections.AcceptConnectionRequest(frodo.OdinId);

        // Sanity: both sides are connected
        var samViewBefore = await sam.Network.GetConnectionInfo(frodo.OdinId);
        ClassicAssert.IsTrue(samViewBefore.IsSuccessStatusCode);
        ClassicAssert.IsTrue(samViewBefore.Content.Status == ConnectionStatus.Connected);

        // Frodo disconnects from Sam using the default (notifyRemote omitted), which now notifies
        // the remote so Sam disconnects too.
        var disconnectResponse = await frodo.Network.DisconnectFrom(sam.OdinId, notifyRemote: true);
        ClassicAssert.IsTrue(disconnectResponse.IsSuccessStatusCode);

        // Frodo's side is gone immediately
        var frodoView = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsTrue(frodoView.Content == null || frodoView.Content.Status != ConnectionStatus.Connected);

        // The reciprocal-disconnect notification is delivered to Sam via Frodo's outbox.
        await frodo.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, TimeSpan.FromSeconds(40));

        // Sam should now also be disconnected from Frodo
        var samViewAfter = await sam.Network.GetConnectionInfo(frodo.OdinId);
        ClassicAssert.IsTrue(samViewAfter.Content == null || samViewAfter.Content.Status != ConnectionStatus.Connected,
            $"Sam was still connected to Frodo after Frodo disconnected (status: {samViewAfter.Content?.Status})");

        // cleanup
        await sam.Network.DisconnectFrom(frodo.OdinId, notifyRemote: false);
    }

    [Test]
    public async Task DisconnectCanOptOutOfNotifyingRemote()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        await sam.Connections.AcceptConnectionRequest(frodo.OdinId);

        // Passing notifyRemote:false must NOT notify the peer -- this preserves the intentional
        // asymmetric-state behavior the connection state machine relies on for callers that need it
        // (e.g. bad-CAT detection scenarios).
        var disconnectResponse = await frodo.Network.DisconnectFrom(sam.OdinId, notifyRemote: false);
        ClassicAssert.IsTrue(disconnectResponse.IsSuccessStatusCode);

        // Frodo's side is gone...
        var frodoView = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsTrue(frodoView.Content == null || frodoView.Content.Status != ConnectionStatus.Connected);

        // ...but Sam still considers itself connected to Frodo (no reciprocal notification sent).
        var samView = await sam.Network.GetConnectionInfo(frodo.OdinId);
        ClassicAssert.IsTrue(samView.IsSuccessStatusCode);
        ClassicAssert.IsTrue(samView.Content.Status == ConnectionStatus.Connected,
            $"Sam should still be connected to Frodo after a one-sided disconnect (status: {samView.Content?.Status})");

        // cleanup
        await sam.Network.DisconnectFrom(frodo.OdinId, notifyRemote: false);
    }
}
