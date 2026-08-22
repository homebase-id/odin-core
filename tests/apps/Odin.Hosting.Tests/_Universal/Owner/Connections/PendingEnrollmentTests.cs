using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests._Universal.Owner.Connections;

/// <summary>
/// Cat 4 -- the cross-app pending queue.  A reviewing client holds App Keys for its own suite only,
/// so a checked toggle for another app cannot enrol on the spot; the decision is recorded and that
/// app completes it when it next runs.
/// </summary>
public class PendingEnrollmentTests
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
    public async Task AReviewFromOneAppQueuesAnotherAppsCircleRatherThanFailing()
    {
        // Before the queue existed this threw: minting the mail circle needs the storage key of a drive
        // the chat client cannot reach, so the whole review failed on one unreachable toggle.
        var (frodo, sam) = await Connect();
        var (mailAppId, mailCircleId) = await RegisterAppWithReadCircle(frodo, "mail");
        var chatApp = await RegisterReviewerApp(frodo);

        await frodo.Network.UnreviewConnection(sam.OdinId);

        var review = await chatApp.ReviewConnection(sam.OdinId, [mailCircleId]);
        ClassicAssert.IsTrue(review.IsSuccessStatusCode,
            $"a review must not fail on a circle the client cannot mint: {review.Error?.Content}");

        var info = await frodo.Network.GetConnectionInfo(sam.OdinId);

        ClassicAssert.IsNotNull(info.Content.ReviewedAt, "the review itself still happened");
        ClassicAssert.IsTrue(info.Content.PendingCircleEnrollments.Contains(mailCircleId),
            "the decision must be recorded as pending");
        ClassicAssert.IsFalse(info.Content.AccessGrant.CircleGrants.Exists(g => g.CircleId == mailCircleId),
            "and must not have taken effect yet -- the dialog must not claim access that does not exist");

        await Cleanup();
    }

    [Test]
    public async Task TheOwningAppCompletesItsQueueWhenItRuns()
    {
        var (frodo, sam) = await Connect();
        var (mailAppId, mailCircleId) = await RegisterAppWithReadCircle(frodo, "mail");
        var chatApp = await RegisterReviewerApp(frodo);

        await frodo.Network.UnreviewConnection(sam.OdinId);
        await chatApp.ReviewConnection(sam.OdinId, [mailCircleId]);

        // Now the mail app runs, with its own App Key in scope.
        var mailApp = await AppClientFor(frodo, mailAppId);
        var completed = await mailApp.ProcessPendingEnrollments();

        ClassicAssert.IsTrue(completed.IsSuccessStatusCode, $"drain failed: {completed.Error?.Content}");
        ClassicAssert.AreEqual(1, completed.Content, "the mail app should have completed one enrollment");

        var info = await frodo.Network.GetConnectionInfo(sam.OdinId);

        // An app holds no master key, so the grant lands as a sealed deposit rather than a live circle
        // grant -- it converts the next time the connection's key store key is in scope, via peer CAT
        // auth or the owner's next grant touch. Either shape counts as completed.
        var granted = info.Content.AccessGrant.CircleGrants.Exists(g => g.CircleId == mailCircleId);
        var deposited = info.Content.AccessGrant.PendingCircleIds?.Contains(mailCircleId) ?? false;

        ClassicAssert.IsTrue(granted || deposited,
            "the enrollment should have produced a grant or a deposit for it");
        ClassicAssert.IsFalse(info.Content.PendingCircleEnrollments.Contains(mailCircleId),
            "and the review-time entry should be cleared -- it is the app's problem now, not the review's");

        await Cleanup();
    }

    [Test]
    public async Task DrainingIsIdempotent()
    {
        var (frodo, sam) = await Connect();
        var (mailAppId, mailCircleId) = await RegisterAppWithReadCircle(frodo, "mail");
        var chatApp = await RegisterReviewerApp(frodo);

        await frodo.Network.UnreviewConnection(sam.OdinId);
        await chatApp.ReviewConnection(sam.OdinId, [mailCircleId]);

        var mailApp = await AppClientFor(frodo, mailAppId);

        var first = await mailApp.ProcessPendingEnrollments();
        ClassicAssert.AreEqual(1, first.Content);

        var second = await mailApp.ProcessPendingEnrollments();
        ClassicAssert.IsTrue(second.IsSuccessStatusCode);
        ClassicAssert.AreEqual(0, second.Content, "a second run has nothing left to do");

        await Cleanup();
    }

    [Test]
    public async Task AnAppOnlyDrainsItsOwnEntries()
    {
        var (frodo, sam) = await Connect();
        var (mailAppId, mailCircleId) = await RegisterAppWithReadCircle(frodo, "mail");
        var chatApp = await RegisterReviewerApp(frodo);

        await frodo.Network.UnreviewConnection(sam.OdinId);
        await chatApp.ReviewConnection(sam.OdinId, [mailCircleId]);

        // A third app runs. Mail's entry is none of its business.
        var otherApp = await RegisterReviewerApp(frodo);
        var completed = await otherApp.ProcessPendingEnrollments();

        ClassicAssert.AreEqual(0, completed.Content, "another app must not complete mail's enrollment");

        var info = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsTrue(info.Content.PendingCircleEnrollments.Contains(mailCircleId),
            "the entry should still be waiting for the app that owns it");

        await Cleanup();
    }

    [Test]
    public async Task TheOwnerNeverQueuesAnything()
    {
        // The owner holds the master key, so every circle is mintable on the spot.
        var (frodo, sam) = await Connect();
        var (_, mailCircleId) = await RegisterAppWithReadCircle(frodo, "mail");

        await frodo.Network.UnreviewConnection(sam.OdinId);

        var review = await frodo.Network.ReviewConnection(sam.OdinId, [mailCircleId]);
        ClassicAssert.IsTrue(review.IsSuccessStatusCode, $"review failed: {review.Error?.Content}");

        var info = await frodo.Network.GetConnectionInfo(sam.OdinId);

        ClassicAssert.IsEmpty(info.Content.PendingCircleEnrollments, "the owner can mint everything");
        ClassicAssert.IsTrue(info.Content.AccessGrant.CircleGrants.Exists(g => g.CircleId == mailCircleId));

        await Cleanup();
    }

    /// <summary>
    /// Registers an app owning a circle that grants read -- so it needs a storage key, and so only that
    /// app can mint it.
    /// </summary>
    private async Task<(Guid appId, Guid circleId)> RegisterAppWithReadCircle(OwnerApiClientRedux frodo, string name)
    {
        var appId = Guid.NewGuid();
        var circleId = Guid.NewGuid();
        var drive = TargetDrive.NewTargetDrive();

        await frodo.DriveManager.CreateDrive(drive, $"{name} drive", "", allowAnonymousReads: false);

        var appPermissions = new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ManageCircleMembership, PermissionKeys.ReadConnections),
            Drives =
            [
                new DriveGrantRequest
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = drive,
                        Permission = DrivePermission.Read | DrivePermission.Write
                    }
                }
            ]
        };

        var response = await frodo.AppManager.RegisterApp(appId, appPermissions, defaultCircles:
        [
            new AppDefaultCircleRequest
            {
                Id = circleId,
                Name = $"{name} readers",
                GrantOn = CircleGrantOn.Review,
                DriveGrants =
                [
                    new DriveGrantRequest
                    {
                        PermissionedDrive = new PermissionedDrive
                        {
                            Drive = drive,
                            Permission = DrivePermission.Read
                        }
                    }
                ]
            }
        ]);

        ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"app registration failed: {response.Error?.Content}");

        return (appId, circleId);
    }

    /// <summary>
    /// Registers an app that can review but owns no circles -- the "reviewing client" of the spec.
    /// </summary>
    private async Task<UniversalCircleNetworkApiClient> RegisterReviewerApp(OwnerApiClientRedux owner)
    {
        var appId = Guid.NewGuid();

        var response = await owner.AppManager.RegisterApp(appId, new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ManageCircleMembership, PermissionKeys.ReadConnections),
            Drives = []
        });

        ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"reviewer app registration failed: {response.Error?.Content}");

        return await AppClientFor(owner, appId);
    }

    private async Task<UniversalCircleNetworkApiClient> AppClientFor(OwnerApiClientRedux owner, Guid appId)
    {
        var (token, sharedSecret) = await owner.AppManager.RegisterAppClient(appId);
        var factory = new ApiClient.Factory.AppApiClientFactory(token, sharedSecret);

        return new UniversalCircleNetworkApiClient(owner.OdinId, factory);
    }

    private async Task<(OwnerApiClientRedux frodo, OwnerApiClientRedux sam)> Connect()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        await sam.Connections.AcceptConnectionRequest(frodo.OdinId);

        return (frodo, sam);
    }

    private async Task Cleanup()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await frodo.Connections.DisconnectFrom(sam.Identity.OdinId);
        await sam.Connections.DisconnectFrom(frodo.Identity.OdinId);
    }
}
