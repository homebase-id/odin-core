using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Services.AppNotifications.Data;
using Odin.Services.AppNotifications.Push.Scheduled;
using Odin.Services.Authorization.Permissions;
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

    /// <summary>
    /// A notification scheduled for the future shows up in the list endpoint with its job id, options,
    /// and send time, and disappears from the list once cancelled.
    /// </summary>
    [Test]
    public async Task ScheduledNotification_AppearsInListUntilCancelled()
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
            Silent = true
        };
        var sendAt = UnixTimeUtc.Now().AddHours(1);

        var scheduleResponse = await scheduleClient.Schedule(options, sendAt);
        ClassicAssert.IsTrue(scheduleResponse.IsSuccessStatusCode, $"Schedule failed: {scheduleResponse.StatusCode}");
        var jobId = scheduleResponse.Content.JobId;

        var listResponse = await scheduleClient.List();
        ClassicAssert.IsTrue(listResponse.IsSuccessStatusCode, $"List failed: {listResponse.StatusCode}");
        var entry = listResponse.Content?.SingleOrDefault(s => s.JobId == jobId);
        ClassicAssert.IsNotNull(entry, "Scheduled notification did not appear in the list");
        ClassicAssert.AreEqual(options.TagId, entry.Options.TagId);
        ClassicAssert.AreEqual(sendAt.milliseconds, entry.SendAt.milliseconds);
        ClassicAssert.AreEqual("Scheduled", entry.State);

        var cancelResponse = await scheduleClient.Cancel(jobId);
        ClassicAssert.IsTrue(cancelResponse.IsSuccessStatusCode, $"Cancel failed: {cancelResponse.StatusCode}");

        var listAfterCancel = await scheduleClient.List();
        ClassicAssert.IsTrue(listAfterCancel.IsSuccessStatusCode, $"List failed: {listAfterCancel.StatusCode}");
        ClassicAssert.IsFalse(
            listAfterCancel.Content?.Any(s => s.JobId == jobId) ?? false,
            "Cancelled notification should no longer appear in the list");
    }

    /// <summary>
    /// Updating a scheduled notification replaces its options and send time in place -- same job id --
    /// and resets its attempt count, rather than requiring a cancel-then-reschedule.
    /// </summary>
    [Test]
    public async Task ScheduledNotification_CanBeUpdatedInPlace()
    {
        var frodo = TestIdentities.Frodo;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);

        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerFrodo.DriveManager.CreateDrive(targetDrive, "Test Drive", "", false);

        var callerContext = new OwnerTestCase(targetDrive);
        await callerContext.Initialize(ownerFrodo);
        var scheduleClient = new ScheduledNotificationV2Client(frodo.OdinId, callerContext.GetFactory());

        var originalOptions = new AppNotificationOptions
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid(),
            Silent = true
        };
        var originalSendAt = UnixTimeUtc.Now().AddHours(1);

        var scheduleResponse = await scheduleClient.Schedule(originalOptions, originalSendAt);
        ClassicAssert.IsTrue(scheduleResponse.IsSuccessStatusCode, $"Schedule failed: {scheduleResponse.StatusCode}");
        var jobId = scheduleResponse.Content.JobId;

        var updatedOptions = new AppNotificationOptions
        {
            AppId = originalOptions.AppId,
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid(),
            Silent = false
        };
        var updatedSendAt = UnixTimeUtc.Now().AddHours(2);

        var updateResponse = await scheduleClient.Update(jobId, updatedOptions, updatedSendAt);
        ClassicAssert.IsTrue(updateResponse.IsSuccessStatusCode, $"Update failed: {updateResponse.StatusCode}");

        var listResponse = await scheduleClient.List();
        ClassicAssert.IsTrue(listResponse.IsSuccessStatusCode, $"List failed: {listResponse.StatusCode}");
        var entry = listResponse.Content?.SingleOrDefault(s => s.JobId == jobId);
        ClassicAssert.IsNotNull(entry, "Updated notification should still exist under the same job id");
        ClassicAssert.AreEqual(updatedOptions.TagId, entry.Options.TagId);
        ClassicAssert.AreEqual(updatedSendAt.milliseconds, entry.SendAt.milliseconds);
        ClassicAssert.AreEqual("Scheduled", entry.State);
        ClassicAssert.AreEqual(0, entry.AttemptCount);
        ClassicAssert.AreEqual(3, entry.MaxAttempts);

        // updating a job id that doesn't exist reports nothing to update
        var updateMissingResponse = await scheduleClient.Update(Guid.NewGuid(), updatedOptions, updatedSendAt);
        ClassicAssert.AreEqual(HttpStatusCode.NotFound, updateMissingResponse.StatusCode);
    }

    /// <summary>
    /// An app can only update the scheduled notifications it created itself; a different app on the same
    /// tenant cannot update it. The owner, however, can update any app's scheduled notification.
    /// </summary>
    [Test]
    public async Task ScheduledNotification_UpdateIsScopedToTheAppThatCreatedIt()
    {
        var frodo = TestIdentities.Frodo;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);

        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerFrodo.DriveManager.CreateDrive(targetDrive, "Test Drive", "", false);

        var appA = new AppTestCase(targetDrive, DrivePermission.Write, new TestPermissionKeyList(PermissionKeys.SendPushNotifications));
        await appA.Initialize(ownerFrodo);
        var appAScheduleClient = new ScheduledNotificationV2Client(frodo.OdinId, appA.GetFactory());

        var appB = new AppTestCase(targetDrive, DrivePermission.Write, new TestPermissionKeyList(PermissionKeys.SendPushNotifications));
        await appB.Initialize(ownerFrodo);
        var appBScheduleClient = new ScheduledNotificationV2Client(frodo.OdinId, appB.GetFactory());

        var options = new AppNotificationOptions
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid(),
            Silent = true
        };
        var sendAt = UnixTimeUtc.Now().AddHours(1);

        // App A schedules a notification.
        var scheduleResponse = await appAScheduleClient.Schedule(options, sendAt);
        ClassicAssert.IsTrue(scheduleResponse.IsSuccessStatusCode, $"Schedule failed: {scheduleResponse.StatusCode}");
        var jobId = scheduleResponse.Content.JobId;

        var updatedOptions = new AppNotificationOptions
        {
            AppId = options.AppId,
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid(),
            Silent = false
        };
        var updatedSendAt = UnixTimeUtc.Now().AddHours(2);

        // App B cannot update App A's scheduled notification.
        var appBUpdate = await appBScheduleClient.Update(jobId, updatedOptions, updatedSendAt);
        ClassicAssert.AreEqual(
            HttpStatusCode.NotFound, appBUpdate.StatusCode,
            "App B should not be able to update App A's scheduled notification");

        // App A can update its own.
        var appAUpdate = await appAScheduleClient.Update(jobId, updatedOptions, updatedSendAt);
        ClassicAssert.IsTrue(appAUpdate.IsSuccessStatusCode, $"App A update failed: {appAUpdate.StatusCode}");

        // The owner can update any app's scheduled notification too.
        var ownerCallerContext = new OwnerTestCase(targetDrive);
        await ownerCallerContext.Initialize(ownerFrodo);
        var ownerScheduleClient = new ScheduledNotificationV2Client(frodo.OdinId, ownerCallerContext.GetFactory());

        var ownerUpdate = await ownerScheduleClient.Update(jobId, updatedOptions, UnixTimeUtc.Now().AddHours(3));
        ClassicAssert.IsTrue(ownerUpdate.IsSuccessStatusCode, $"Owner update failed: {ownerUpdate.StatusCode}");
    }

    /// <summary>
    /// An app can only see/cancel the scheduled notifications it created itself; a different app on the
    /// same tenant sees neither in its list nor can cancel it. The owner, however, sees and can cancel
    /// any app's scheduled notification.
    /// </summary>
    [Test]
    public async Task ScheduledNotification_IsScopedToTheAppThatCreatedIt()
    {
        var frodo = TestIdentities.Frodo;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);

        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerFrodo.DriveManager.CreateDrive(targetDrive, "Test Drive", "", false);

        var appA = new AppTestCase(targetDrive, DrivePermission.Write, new TestPermissionKeyList(PermissionKeys.SendPushNotifications));
        await appA.Initialize(ownerFrodo);
        var appAScheduleClient = new ScheduledNotificationV2Client(frodo.OdinId, appA.GetFactory());

        var appB = new AppTestCase(targetDrive, DrivePermission.Write, new TestPermissionKeyList(PermissionKeys.SendPushNotifications));
        await appB.Initialize(ownerFrodo);
        var appBScheduleClient = new ScheduledNotificationV2Client(frodo.OdinId, appB.GetFactory());

        var options = new AppNotificationOptions
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid(),
            Silent = true
        };
        var sendAt = UnixTimeUtc.Now().AddHours(1);

        // App A schedules a notification.
        var scheduleResponse = await appAScheduleClient.Schedule(options, sendAt);
        ClassicAssert.IsTrue(scheduleResponse.IsSuccessStatusCode, $"Schedule failed: {scheduleResponse.StatusCode}");
        var jobId = scheduleResponse.Content.JobId;

        // App B does not see it in its own list...
        var appBList = await appBScheduleClient.List();
        ClassicAssert.IsTrue(appBList.IsSuccessStatusCode, $"List failed: {appBList.StatusCode}");
        ClassicAssert.IsFalse(
            appBList.Content?.Any(s => s.JobId == jobId) ?? false,
            "App B should not see App A's scheduled notification in its list");

        // ...and cannot cancel it.
        var appBCancel = await appBScheduleClient.Cancel(jobId);
        ClassicAssert.AreEqual(
            HttpStatusCode.NotFound, appBCancel.StatusCode,
            "App B should not be able to cancel App A's scheduled notification");

        // App A still sees its own scheduled notification.
        var appAList = await appAScheduleClient.List();
        ClassicAssert.IsTrue(
            appAList.Content?.Any(s => s.JobId == jobId) ?? false,
            "App A should still see its own scheduled notification");

        // The owner sees every app's scheduled notifications...
        var ownerCallerContext = new OwnerTestCase(targetDrive);
        await ownerCallerContext.Initialize(ownerFrodo);
        var ownerScheduleClient = new ScheduledNotificationV2Client(frodo.OdinId, ownerCallerContext.GetFactory());

        var ownerList = await ownerScheduleClient.List();
        ClassicAssert.IsTrue(
            ownerList.Content?.Any(s => s.JobId == jobId) ?? false,
            "Owner should see App A's scheduled notification");

        // ...and can cancel any of them.
        var ownerCancel = await ownerScheduleClient.Cancel(jobId);
        ClassicAssert.IsTrue(ownerCancel.IsSuccessStatusCode, $"Owner cancel failed: {ownerCancel.StatusCode}");
    }

    /// <summary>
    /// A tenant cannot have more than <see cref="ScheduledNotificationService.MaxPendingPerTenant"/>
    /// scheduled notifications pending at once; scheduling beyond the cap is rejected with a client
    /// error rather than silently growing the shared job queue without limit.
    /// </summary>
    [Test]
    public async Task ScheduledNotification_CannotExceedMaxPendingPerTenant()
    {
        var frodo = TestIdentities.Frodo;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);

        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerFrodo.DriveManager.CreateDrive(targetDrive, "Test Drive", "", false);

        var callerContext = new OwnerTestCase(targetDrive);
        await callerContext.Initialize(ownerFrodo);
        var scheduleClient = new ScheduledNotificationV2Client(frodo.OdinId, callerContext.GetFactory());

        // Far enough out that nothing fires (and drops off the pending count) during the test.
        var sendAt = UnixTimeUtc.Now().AddHours(6);

        // Other tests sharing this identity may have left pending notifications behind; count what's
        // already there rather than assuming a clean slate, so this test isn't order-dependent.
        var listBefore = await scheduleClient.List();
        ClassicAssert.IsTrue(listBefore.IsSuccessStatusCode, $"List failed: {listBefore.StatusCode}");
        var currentCount = listBefore.Content?.Count ?? 0;

        var scheduledJobIds = new List<Guid>();
        try
        {
            // Fill up to (but not over) the cap.
            for (var i = currentCount; i < ScheduledNotificationService.MaxPendingPerTenant; i++)
            {
                var options = new AppNotificationOptions
                {
                    AppId = Guid.NewGuid(),
                    TypeId = Guid.NewGuid(),
                    TagId = Guid.NewGuid(),
                    Silent = true
                };
                var response = await scheduleClient.Schedule(options, sendAt);
                ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Schedule failed while filling the cap: {response.StatusCode}");
                scheduledJobIds.Add(response.Content.JobId);
            }

            // One more, past the cap, is rejected.
            var overCapOptions = new AppNotificationOptions
            {
                AppId = Guid.NewGuid(),
                TypeId = Guid.NewGuid(),
                TagId = Guid.NewGuid(),
                Silent = true
            };
            var overCapResponse = await scheduleClient.Schedule(overCapOptions, sendAt);
            ClassicAssert.AreEqual(HttpStatusCode.BadRequest, overCapResponse.StatusCode);
            ClassicAssert.AreEqual(
                OdinClientErrorCode.TooManyScheduledNotifications, WebScaffold.GetErrorCode(overCapResponse.Error));
        }
        finally
        {
            // Don't leave the tenant pinned at the cap for whichever test runs next.
            foreach (var jobId in scheduledJobIds)
            {
                await scheduleClient.Cancel(jobId);
            }
        }
    }

    /// <summary>
    /// Both Schedule and Update require notification options; a null payload is rejected with a client
    /// error rather than silently scheduling/updating with no content.
    /// </summary>
    [Test]
    public async Task ScheduledNotification_RequiresOptions()
    {
        var frodo = TestIdentities.Frodo;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);

        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerFrodo.DriveManager.CreateDrive(targetDrive, "Test Drive", "", false);

        var callerContext = new OwnerTestCase(targetDrive);
        await callerContext.Initialize(ownerFrodo);
        var scheduleClient = new ScheduledNotificationV2Client(frodo.OdinId, callerContext.GetFactory());

        var sendAt = UnixTimeUtc.Now().AddHours(1);

        // Schedule with no options is rejected.
        var scheduleResponse = await scheduleClient.Schedule(null, sendAt);
        ClassicAssert.AreEqual(HttpStatusCode.BadRequest, scheduleResponse.StatusCode);
        ClassicAssert.AreEqual(OdinClientErrorCode.ArgumentError, WebScaffold.GetErrorCode(scheduleResponse.Error));

        // Schedule a real notification so there's a job id to attempt an update against.
        var options = new AppNotificationOptions
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid(),
            Silent = true
        };
        var validScheduleResponse = await scheduleClient.Schedule(options, sendAt);
        ClassicAssert.IsTrue(validScheduleResponse.IsSuccessStatusCode, $"Schedule failed: {validScheduleResponse.StatusCode}");
        var jobId = validScheduleResponse.Content.JobId;

        // Update with no options is rejected too.
        var updateResponse = await scheduleClient.Update(jobId, null, sendAt);
        ClassicAssert.AreEqual(HttpStatusCode.BadRequest, updateResponse.StatusCode);
        ClassicAssert.AreEqual(OdinClientErrorCode.ArgumentError, WebScaffold.GetErrorCode(updateResponse.Error));

        await scheduleClient.Cancel(jobId);
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
