using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests._Universal.Owner.Connections;

/// <summary>
/// Cat 3.6-3.10: a connection is enrolled in the grant-on-connect circles of every app the owner has
/// left enabled, resolved at connect time rather than from a frozen list.
/// </summary>
public class AmbientEnrollmentTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>
        {
            TestIdentities.Frodo,
            TestIdentities.Samwise
        });
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

    [Test]
    public async Task TheChatAppsConnectCircleIsAppliedToANewConnection()
    {
        // The chat app declares a Chat-only circle at registration; a connection formed afterwards
        // should hold it without anyone naming it.
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await Connect(frodo, sam);

        var info = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsTrue(info.IsSuccessStatusCode);

        ClassicAssert.IsTrue(
            info.Content.AccessGrant.CircleGrants.Exists(g => g.CircleId == SystemAppConstants.ChatConnectCircleId),
            "a new connection should be enrolled in the chat app's grant-on-connect circle");

        await Cleanup(frodo, sam);
    }

    [Test]
    public async Task TurningAnAppOffStopsFutureConnectionsFromBeingEnrolled()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var off = await frodo.AppManager.SetConnectEnrollment(SystemAppConstants.ChatAppId, enabled: false);
        ClassicAssert.IsTrue(off.IsSuccessStatusCode, $"toggle failed: {off.Error?.Content}");

        try
        {
            await Connect(frodo, sam);

            var info = await frodo.Network.GetConnectionInfo(sam.OdinId);
            ClassicAssert.IsFalse(
                info.Content.AccessGrant.CircleGrants.Exists(g => g.CircleId == SystemAppConstants.ChatConnectCircleId),
                "a disabled app's circle must not be applied");

            await Cleanup(frodo, sam);
        }
        finally
        {
            await frodo.AppManager.SetConnectEnrollment(SystemAppConstants.ChatAppId, enabled: true);
        }
    }

    [Test]
    public async Task TurningAnAppOffLeavesAlreadyEnrolledIdentitiesAlone()
    {
        // Future connections only. Revoking existing membership is a separate, explicit action.
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await Connect(frodo, sam);

        var before = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsTrue(
            before.Content.AccessGrant.CircleGrants.Exists(g => g.CircleId == SystemAppConstants.ChatConnectCircleId));

        await frodo.AppManager.SetConnectEnrollment(SystemAppConstants.ChatAppId, enabled: false);

        try
        {
            var after = await frodo.Network.GetConnectionInfo(sam.OdinId);
            ClassicAssert.IsTrue(
                after.Content.AccessGrant.CircleGrants.Exists(g => g.CircleId == SystemAppConstants.ChatConnectCircleId),
                "an existing connection keeps its grant when the app is switched off");
        }
        finally
        {
            await frodo.AppManager.SetConnectEnrollment(SystemAppConstants.ChatAppId, enabled: true);
            await Cleanup(frodo, sam);
        }
    }

    [Test]
    public async Task AnAppDefaultCircleDoesNotBlockUnReview()
    {
        // Being in an app's default circle is not evidence the owner reviewed anyone -- it happened
        // automatically. Only a circle the owner deliberately chose should stand in the way.
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await Connect(frodo, sam);

        var info = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsTrue(
            info.Content.AccessGrant.CircleGrants.Exists(g => g.CircleId == SystemAppConstants.ChatConnectCircleId),
            "precondition: they hold an app default circle");

        var unreview = await frodo.Network.UnreviewConnection(sam.OdinId);
        ClassicAssert.IsTrue(unreview.IsSuccessStatusCode,
            $"an app default circle must not block un-review: {unreview.Error?.Content}");

        await Cleanup(frodo, sam);
    }

    [Test]
    public async Task ADeliberateCircleStillBlocksUnReview()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await Connect(frodo, sam);

        var circleId = Guid.NewGuid();
        await frodo.Network.CreateCircle(circleId, "Friends", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        await frodo.Network.ReviewConnection(sam.OdinId, [circleId]);

        var unreview = await frodo.Network.UnreviewConnection(sam.OdinId);
        ClassicAssert.IsFalse(unreview.IsSuccessStatusCode,
            "a circle the owner chose must still block un-review");

        await Cleanup(frodo, sam);
    }

    private static async Task Connect(
        ApiClient.Owner.OwnerApiClientRedux frodo,
        ApiClient.Owner.OwnerApiClientRedux sam)
    {
        await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        await sam.Connections.AcceptConnectionRequest(frodo.OdinId);
    }

    private static async Task Cleanup(
        ApiClient.Owner.OwnerApiClientRedux frodo,
        ApiClient.Owner.OwnerApiClientRedux sam)
    {
        await frodo.Connections.DisconnectFrom(sam.Identity.OdinId);
        await sam.Connections.DisconnectFrom(frodo.Identity.OdinId);
    }
}
