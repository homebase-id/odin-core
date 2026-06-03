using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Services.AppNotifications.Data;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._V2.Tests.Notifications;

[TestFixture]
public class ScheduledNotificationTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity> { TestIdentities.Frodo });
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

    /// <summary>
    /// End-to-end: an owner schedules a notification to fire "now"; the background job runner picks it up,
    /// runs the job, which enqueues the notification so it lands in the owner's notification list.
    /// (As with PeerNotificationTests, we don't assert the device push itself went out -- that's a separate
    /// set of dependencies -- only that the notification reaches the list.)
    /// </summary>
    [Test]
    public async Task ScheduledNotification_FiresAndLandsInNotificationList()
    {
        var frodo = TestIdentities.Frodo;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);

        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerFrodo.DriveManager.CreateDrive(targetDrive, "Test Drive", "", false);

        var callerContext = new OwnerTestCase(targetDrive);
        await callerContext.Initialize(ownerFrodo);
        var scheduleClient = new ScheduledNotificationV2Client(frodo.OdinId, callerContext.GetFactory());

        var options = new AppNotificationOptions
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid(),
            Silent = true,
            UnEncryptedMessage = "scheduled hello"
        };

        // fire as soon as possible
        var scheduleResponse = await scheduleClient.Schedule(options, UnixTimeUtc.Now());
        ClassicAssert.IsTrue(scheduleResponse.IsSuccessStatusCode, $"Schedule failed: {scheduleResponse.StatusCode}");
        ClassicAssert.AreNotEqual(Guid.Empty, scheduleResponse.Content.JobId);

        // the job runs in the background; poll the list until it arrives
        var notification = await WaitForNotificationByTagId(ownerFrodo, options.TagId, TimeSpan.FromSeconds(20));

        ClassicAssert.IsNotNull(notification, "Scheduled notification never reached the notification list");
        ClassicAssert.IsTrue(notification.SenderId == frodo.OdinId);
        ClassicAssert.AreEqual(options.AppId, notification.Options.AppId);
        ClassicAssert.AreEqual(options.TypeId, notification.Options.TypeId);
        ClassicAssert.AreEqual(options.Silent, notification.Options.Silent);
        ClassicAssert.AreEqual(options.TagId, notification.Options.TagId);
    }

    /// <summary>
    /// End-to-end: a notification scheduled for the future can be cancelled before it fires.
    /// Cancelling deletes the underlying job (a second cancel returns 404), and the notification
    /// never lands in the list.
    /// </summary>
    [Test]
    public async Task ScheduledNotification_CanBeCancelledBeforeItFires()
    {
        var frodo = TestIdentities.Frodo;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);

        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerFrodo.DriveManager.CreateDrive(targetDrive, "Test Drive", "", false);

        var callerContext = new OwnerTestCase(targetDrive);
        await callerContext.Initialize(ownerFrodo);
        var scheduleClient = new ScheduledNotificationV2Client(frodo.OdinId, callerContext.GetFactory());

        var options = new AppNotificationOptions
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid(),
            Silent = false
        };

        // schedule far enough in the future that it will not fire during the test
        var scheduleResponse = await scheduleClient.Schedule(options, UnixTimeUtc.Now().AddHours(1));
        ClassicAssert.IsTrue(scheduleResponse.IsSuccessStatusCode, $"Schedule failed: {scheduleResponse.StatusCode}");
        var jobId = scheduleResponse.Content.JobId;
        ClassicAssert.AreNotEqual(Guid.Empty, jobId);

        // cancel succeeds...
        var cancelResponse = await scheduleClient.Cancel(jobId);
        ClassicAssert.IsTrue(cancelResponse.IsSuccessStatusCode, $"Cancel failed: {cancelResponse.StatusCode}");

        // ...and the job is gone: a second cancel reports nothing to cancel
        var cancelAgainResponse = await scheduleClient.Cancel(jobId);
        ClassicAssert.AreEqual(HttpStatusCode.NotFound, cancelAgainResponse.StatusCode);

        // and it never reached the notification list
        var notification = await WaitForNotificationByTagId(ownerFrodo, options.TagId, TimeSpan.FromSeconds(2));
        ClassicAssert.IsNull(notification, "A cancelled notification should not be delivered");
    }

    private static async Task<AppNotification> WaitForNotificationByTagId(
        OwnerApiClientRedux ownerClient,
        Guid tagId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            var response = await ownerClient.AppNotifications.GetList(1000);
            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"GetList failed: {response.StatusCode}");
            var match = response.Content?.Results?.SingleOrDefault(n => n.Options.TagId == tagId);
            if (match != null)
            {
                return match;
            }

            await Task.Delay(250);
        } while (DateTime.UtcNow < deadline);

        return null;
    }
}
