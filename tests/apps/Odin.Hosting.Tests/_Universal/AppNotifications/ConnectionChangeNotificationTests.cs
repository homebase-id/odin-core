using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;

namespace Odin.Hosting.Tests._Universal.AppNotifications;

// Verifies that connection state changes (circle grant/revoke, block/unblock, disconnect) push a
// ConnectionChanged (5002) notification to the owner's own connected sessions, carrying the affected
// identity and (for circle grant/revoke) the circle id. Each test uses a distinct peer so the shared
// server's connection state does not couple tests together.
public class ConnectionChangeNotificationTests
{
    private WebScaffold _scaffold;

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>
        {
            TestIdentities.Frodo,
            TestIdentities.Samwise,
            TestIdentities.Merry,
            TestIdentities.Pippin
        });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _scaffold.RunAfterAnyTests();

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown() => _scaffold.AssertLogEvents();

    private static async Task Connect(OwnerApiClientRedux a, OwnerApiClientRedux b)
    {
        await a.Connections.SendConnectionRequest(b.OdinId, []);
        await b.Connections.AcceptConnectionRequest(a.OdinId);
    }

    [Test]
    public async Task GrantAndRevokeCircle_PushesConnectionChangedWithCircleId()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await Connect(frodo, sam);

        // Create the circle before opening the socket so its CircleDefinitionChanged{Created}
        // doesn't share the capture window with the ConnectionChanged events under test.
        var circleId = Guid.NewGuid();
        var createResponse = await frodo.Network.CreateCircle(circleId, "ws-grant-circle",
            new PermissionSetGrantRequest { PermissionSet = new(PermissionKeys.AllowIntroductions) });
        ClassicAssert.IsTrue(createResponse.IsSuccessStatusCode);

        var handler = new ConnectionCircleNotificationSocketHandler();
        await handler.ConnectAsync(frodo);
        try
        {
            var grantResponse = await frodo.Network.GrantCircle(circleId, sam.OdinId);
            ClassicAssert.IsTrue(grantResponse.IsSuccessStatusCode,
                $"Grant failed: {grantResponse.StatusCode}");

            var granted = await handler.WaitForConnectionChange(
                ConnectionChangeType.CircleGranted, sam.OdinId, WaitTimeout);
            ClassicAssert.IsNotNull(granted, "Expected a ConnectionChanged{CircleGranted} notification");
            ClassicAssert.AreEqual(circleId, granted.CircleId);

            var revokeResponse = await frodo.Network.RevokeCircle(circleId, sam.OdinId);
            ClassicAssert.IsTrue(revokeResponse.IsSuccessStatusCode);

            var revoked = await handler.WaitForConnectionChange(
                ConnectionChangeType.CircleRevoked, sam.OdinId, WaitTimeout);
            ClassicAssert.IsNotNull(revoked, "Expected a ConnectionChanged{CircleRevoked} notification");
            ClassicAssert.AreEqual(circleId, revoked.CircleId);
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    [Test]
    public async Task BlockAndUnblock_PushesConnectionChanged()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var merry = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        await Connect(frodo, merry);

        var handler = new ConnectionCircleNotificationSocketHandler();
        await handler.ConnectAsync(frodo);
        try
        {
            var blockResponse = await frodo.Network.BlockConnection(merry.OdinId);
            ClassicAssert.IsTrue(blockResponse.IsSuccessStatusCode);

            var blocked = await handler.WaitForConnectionChange(
                ConnectionChangeType.Blocked, merry.OdinId, WaitTimeout);
            ClassicAssert.IsNotNull(blocked, "Expected a ConnectionChanged{Blocked} notification");
            ClassicAssert.IsNull(blocked.CircleId);

            var unblockResponse = await frodo.Network.UnblockConnection(merry.OdinId);
            ClassicAssert.IsTrue(unblockResponse.IsSuccessStatusCode);

            var unblocked = await handler.WaitForConnectionChange(
                ConnectionChangeType.Unblocked, merry.OdinId, WaitTimeout);
            ClassicAssert.IsNotNull(unblocked, "Expected a ConnectionChanged{Unblocked} notification");
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    [Test]
    public async Task Disconnect_PushesConnectionChanged()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var pippin = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);

        await Connect(frodo, pippin);

        var handler = new ConnectionCircleNotificationSocketHandler();
        await handler.ConnectAsync(frodo);
        try
        {
            var disconnectResponse = await frodo.Network.DisconnectFrom(pippin.OdinId);
            ClassicAssert.IsTrue(disconnectResponse.IsSuccessStatusCode);

            var disconnected = await handler.WaitForConnectionChange(
                ConnectionChangeType.Disconnected, pippin.OdinId, WaitTimeout);
            ClassicAssert.IsNotNull(disconnected, "Expected a ConnectionChanged{Disconnected} notification");
            ClassicAssert.IsNull(disconnected.CircleId);
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }
}
