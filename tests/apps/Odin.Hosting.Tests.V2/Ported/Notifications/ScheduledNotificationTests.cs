using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.AppNotifications.Push.Scheduled;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.JobManagement;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Notifications;

/// <summary>
/// Port of the REST half of <c>_V2/Tests/Notifications/ScheduledNotificationTests</c>: schedule,
/// list, cancel, update-in-place, per-app scoping, the per-tenant pending cap, the recurrence floor,
/// and the null-options guard.
/// </summary>
/// <remarks>
/// The two cases that assert on a job actually firing stay on WebScaffold (the fast framework never
/// starts background services) — see the FLAGGED banner on the original for which and why.
///
/// Scheduled jobs live in the SYSTEM database, which <see cref="V2Fixture.ResetBetweenTests"/> does
/// not restore — it snapshots the identity DB and payload tree only — so they would leak from test to
/// test inside this fixture. <see cref="ClearScheduledJobs"/> wipes them per test, which is also why
/// no test cancels what it scheduled on the way out.
/// </remarks>
[TestFixture]
public class ScheduledNotificationTests : V2Fixture
{
    /// <summary>
    /// Runs after <c>V2Fixture</c>'s own reset (NUnit walks [SetUp] base-first). Deletes this tenant's
    /// jobs directly: <c>DeleteJobsByIdentityIdAsync</c> touches the jobs table without notifying the
    /// job runner, so it is safe in a host where background services are never started.
    /// </summary>
    [SetUp]
    public async Task ClearScheduledJobs()
    {
        var scope = Host.GetTenantScope(Identities.Frodo);
        var jobManager = scope.Resolve<IJobManager>();
        var tenantContext = scope.Resolve<TenantContext>();
        await jobManager.DeleteJobsByIdentityIdAsync(tenantContext.DotYouRegistryId);
    }

    private static ScheduledNotificationV2Client ScheduleClient(IV2Caller caller) =>
        new(caller.Identity, caller.Factory);

    private static AppNotificationOptions NewOptions(Guid? appId = null, bool silent = true) => new()
    {
        AppId = appId ?? Guid.NewGuid(),
        TypeId = Guid.NewGuid(),
        TagId = Guid.NewGuid(),
        Silent = silent
    };

    private static void AssertClientError<T>(ApiResponse<T> response, OdinClientErrorCode expected)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(WebScaffold.GetErrorCode(response.Error), Is.EqualTo(expected));
    }

    /// <summary>
    /// Two apps on one tenant, both granted <see cref="PermissionKeys.SendPushNotifications"/>. The
    /// drive exists only because an app registration needs one to hang its grant on; no test here
    /// touches it.
    /// </summary>
    private async Task<(OwnerSession Owner, ScheduledNotificationV2Client AppA, ScheduledNotificationV2Client AppB)>
        SetupTwoPushAppsAsync()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var targetDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.EnsureDrive(targetDrive, "Test Drive");

        var appA = await AppSession.SetupAsync(owner, targetDrive, DrivePermission.Write,
            [PermissionKeys.SendPushNotifications]);
        var appB = await AppSession.SetupAsync(owner, targetDrive, DrivePermission.Write,
            [PermissionKeys.SendPushNotifications]);

        return (owner, ScheduleClient(appA), ScheduleClient(appB));
    }

    /// <summary>
    /// A notification scheduled for the future shows up in the list endpoint with its job id, options,
    /// and send time, and disappears from the list once cancelled.
    /// </summary>
    [Test]
    public async Task ScheduledNotification_AppearsInListUntilCancelled()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scheduleClient = ScheduleClient(owner);

        var options = NewOptions();
        var sendAt = UnixTimeUtc.Now().AddHours(1);

        var scheduleResponse = await scheduleClient.Schedule(options, sendAt);
        Assert.That(scheduleResponse.IsSuccessStatusCode, Is.True, $"Schedule failed: {scheduleResponse.StatusCode}");
        var jobId = scheduleResponse.Content!.JobId;
        Assert.That(jobId, Is.Not.EqualTo(Guid.Empty));

        var listResponse = await scheduleClient.List();
        Assert.That(listResponse.IsSuccessStatusCode, Is.True, $"List failed: {listResponse.StatusCode}");
        var entry = listResponse.Content?.SingleOrDefault(s => s.JobId == jobId);
        Assert.That(entry, Is.Not.Null, "Scheduled notification did not appear in the list");
        Assert.That(entry!.Options.TagId, Is.EqualTo(options.TagId));
        Assert.That(entry.SendAt.milliseconds, Is.EqualTo(sendAt.milliseconds));
        Assert.That(entry.State, Is.EqualTo("Scheduled"));

        var cancelResponse = await scheduleClient.Cancel(jobId);
        Assert.That(cancelResponse.IsSuccessStatusCode, Is.True, $"Cancel failed: {cancelResponse.StatusCode}");

        // The job is gone: it leaves the list, and a second cancel reports nothing to cancel.
        var listAfterCancel = await scheduleClient.List();
        Assert.That(listAfterCancel.IsSuccessStatusCode, Is.True, $"List failed: {listAfterCancel.StatusCode}");
        Assert.That(listAfterCancel.Content!.Select(s => s.JobId), Does.Not.Contain(jobId),
            "Cancelled notification should no longer appear in the list");

        var cancelAgainResponse = await scheduleClient.Cancel(jobId);
        Assert.That(cancelAgainResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// Updating a scheduled notification replaces its options and send time in place -- same job id --
    /// and resets its attempt count, rather than requiring a cancel-then-reschedule.
    /// </summary>
    [Test]
    public async Task ScheduledNotification_CanBeUpdatedInPlace()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scheduleClient = ScheduleClient(owner);

        var originalOptions = NewOptions();
        var scheduleResponse = await scheduleClient.Schedule(originalOptions, UnixTimeUtc.Now().AddHours(1));
        Assert.That(scheduleResponse.IsSuccessStatusCode, Is.True, $"Schedule failed: {scheduleResponse.StatusCode}");
        var jobId = scheduleResponse.Content!.JobId;

        var updatedOptions = NewOptions(appId: originalOptions.AppId, silent: false);
        var updatedSendAt = UnixTimeUtc.Now().AddHours(2);

        var updateResponse = await scheduleClient.Update(jobId, updatedOptions, updatedSendAt);
        Assert.That(updateResponse.IsSuccessStatusCode, Is.True, $"Update failed: {updateResponse.StatusCode}");

        var listResponse = await scheduleClient.List();
        Assert.That(listResponse.IsSuccessStatusCode, Is.True, $"List failed: {listResponse.StatusCode}");
        var entry = listResponse.Content?.SingleOrDefault(s => s.JobId == jobId);
        Assert.That(entry, Is.Not.Null, "Updated notification should still exist under the same job id");
        Assert.That(entry!.Options.TagId, Is.EqualTo(updatedOptions.TagId));
        Assert.That(entry.SendAt.milliseconds, Is.EqualTo(updatedSendAt.milliseconds));
        Assert.That(entry.State, Is.EqualTo("Scheduled"));
        Assert.That(entry.AttemptCount, Is.EqualTo(0));
        Assert.That(entry.MaxAttempts, Is.EqualTo(3));

        // updating a job id that doesn't exist reports nothing to update
        var updateMissingResponse = await scheduleClient.Update(Guid.NewGuid(), updatedOptions, updatedSendAt);
        Assert.That(updateMissingResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// An app can only update the scheduled notifications it created itself; a different app on the same
    /// tenant cannot update it. The owner, however, can update any app's scheduled notification.
    /// </summary>
    [Test]
    public async Task ScheduledNotification_UpdateIsScopedToTheAppThatCreatedIt()
    {
        var (owner, appAScheduleClient, appBScheduleClient) = await SetupTwoPushAppsAsync();

        var options = NewOptions();

        // App A schedules a notification.
        var scheduleResponse = await appAScheduleClient.Schedule(options, UnixTimeUtc.Now().AddHours(1));
        Assert.That(scheduleResponse.IsSuccessStatusCode, Is.True, $"Schedule failed: {scheduleResponse.StatusCode}");
        var jobId = scheduleResponse.Content!.JobId;

        var updatedOptions = NewOptions(appId: options.AppId, silent: false);
        var updatedSendAt = UnixTimeUtc.Now().AddHours(2);

        // App B cannot update App A's scheduled notification.
        var appBUpdate = await appBScheduleClient.Update(jobId, updatedOptions, updatedSendAt);
        Assert.That(appBUpdate.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "App B should not be able to update App A's scheduled notification");

        // App A can update its own.
        var appAUpdate = await appAScheduleClient.Update(jobId, updatedOptions, updatedSendAt);
        Assert.That(appAUpdate.IsSuccessStatusCode, Is.True, $"App A update failed: {appAUpdate.StatusCode}");

        // The owner can update any app's scheduled notification too.
        var ownerUpdate = await ScheduleClient(owner).Update(jobId, updatedOptions, UnixTimeUtc.Now().AddHours(3));
        Assert.That(ownerUpdate.IsSuccessStatusCode, Is.True, $"Owner update failed: {ownerUpdate.StatusCode}");
    }

    /// <summary>
    /// An app can only see/cancel the scheduled notifications it created itself; a different app on the
    /// same tenant sees neither in its list nor can cancel it. The owner, however, sees and can cancel
    /// any app's scheduled notification.
    /// </summary>
    [Test]
    public async Task ScheduledNotification_IsScopedToTheAppThatCreatedIt()
    {
        var (owner, appAScheduleClient, appBScheduleClient) = await SetupTwoPushAppsAsync();

        // App A schedules a notification.
        var scheduleResponse = await appAScheduleClient.Schedule(NewOptions(), UnixTimeUtc.Now().AddHours(1));
        Assert.That(scheduleResponse.IsSuccessStatusCode, Is.True, $"Schedule failed: {scheduleResponse.StatusCode}");
        var jobId = scheduleResponse.Content!.JobId;

        // App B does not see it in its own list...
        var appBList = await appBScheduleClient.List();
        Assert.That(appBList.IsSuccessStatusCode, Is.True, $"List failed: {appBList.StatusCode}");
        Assert.That(appBList.Content!.Select(s => s.JobId), Does.Not.Contain(jobId),
            "App B should not see App A's scheduled notification in its list");

        // ...and cannot cancel it.
        var appBCancel = await appBScheduleClient.Cancel(jobId);
        Assert.That(appBCancel.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "App B should not be able to cancel App A's scheduled notification");

        // App A still sees its own scheduled notification.
        var appAList = await appAScheduleClient.List();
        Assert.That(appAList.Content!.Select(s => s.JobId), Does.Contain(jobId),
            "App A should still see its own scheduled notification");

        // The owner sees every app's scheduled notifications...
        var ownerScheduleClient = ScheduleClient(owner);
        var ownerList = await ownerScheduleClient.List();
        Assert.That(ownerList.Content!.Select(s => s.JobId), Does.Contain(jobId),
            "Owner should see App A's scheduled notification");

        // ...and can cancel any of them.
        var ownerCancel = await ownerScheduleClient.Cancel(jobId);
        Assert.That(ownerCancel.IsSuccessStatusCode, Is.True, $"Owner cancel failed: {ownerCancel.StatusCode}");
    }

    /// <summary>
    /// A tenant cannot have more than <see cref="ScheduledNotificationService.MaxPendingPerTenant"/>
    /// scheduled notifications pending at once; scheduling beyond the cap is rejected with a client
    /// error rather than silently growing the shared job queue without limit.
    /// </summary>
    [Test]
    public async Task ScheduledNotification_CannotExceedMaxPendingPerTenant()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scheduleClient = ScheduleClient(owner);

        // Far enough out that nothing fires (and drops off the pending count) during the test.
        var sendAt = UnixTimeUtc.Now().AddHours(6);

        // ClearScheduledJobs leaves the tenant empty; count rather than assume, so this still holds
        // if that ever changes. The cap is a const on the service and cannot be lowered from config,
        // so this really does fill 100 slots.
        var listBefore = await scheduleClient.List();
        Assert.That(listBefore.IsSuccessStatusCode, Is.True, $"List failed: {listBefore.StatusCode}");
        var currentCount = listBefore.Content?.Count ?? 0;

        // Fill up to (but not over) the cap.
        for (var i = currentCount; i < ScheduledNotificationService.MaxPendingPerTenant; i++)
        {
            var response = await scheduleClient.Schedule(NewOptions(), sendAt);
            Assert.That(response.IsSuccessStatusCode, Is.True,
                $"Schedule failed while filling the cap: {response.StatusCode}");
        }

        // One more, past the cap, is rejected.
        var overCapResponse = await scheduleClient.Schedule(NewOptions(), sendAt);
        AssertClientError(overCapResponse, OdinClientErrorCode.TooManyScheduledNotifications);
    }

    /// <summary>
    /// A recurrence interval below <see cref="ScheduledNotificationService.MinRecurrenceInterval"/> is
    /// rejected on both Schedule and Update -- this floor, not the pending-count cap, is what actually
    /// protects the shared job queue from a single recurring notification firing too often.
    /// </summary>
    [Test]
    public async Task ScheduledNotification_RejectsRecurrenceIntervalBelowFloor()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scheduleClient = ScheduleClient(owner);

        var options = NewOptions();
        var sendAt = UnixTimeUtc.Now().AddHours(1);
        var tooShortInterval = ScheduledNotificationService.MinRecurrenceInterval - 1;

        // Rejected on Schedule.
        var scheduleResponse = await scheduleClient.Schedule(options, sendAt, tooShortInterval);
        AssertClientError(scheduleResponse, OdinClientErrorCode.ArgumentError);

        // A valid one-shot schedule, then rejected on Update too.
        var validResponse = await scheduleClient.Schedule(options, sendAt);
        Assert.That(validResponse.IsSuccessStatusCode, Is.True, $"Schedule failed: {validResponse.StatusCode}");
        var jobId = validResponse.Content!.JobId;

        var updateResponse = await scheduleClient.Update(jobId, options, sendAt, tooShortInterval);
        AssertClientError(updateResponse, OdinClientErrorCode.ArgumentError);

    }

    /// <summary>
    /// A recurring notification is surfaced in the list with its interval, and counts as 2 against
    /// <see cref="ScheduledNotificationService.MaxPendingPerTenant"/> instead of 1, since it never ages
    /// out of the queue on its own the way a one-shot notification does.
    /// </summary>
    [Test]
    public async Task ScheduledNotification_RecurringNotificationCountsDoubleAgainstTheCap()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scheduleClient = ScheduleClient(owner);

        var sendAt = UnixTimeUtc.Now().AddHours(6);

        // Work out the current weight (one-shot = 1, recurring = 2) rather than assuming a clean slate.
        var listBefore = await scheduleClient.List();
        Assert.That(listBefore.IsSuccessStatusCode, Is.True, $"List failed: {listBefore.StatusCode}");
        var currentWeight = listBefore.Content?.Sum(e => e.RecurrenceInterval != null ? 2 : 1) ?? 0;

        var recurringResponse = await scheduleClient.Schedule(
            NewOptions(), sendAt, ScheduledNotificationService.MinRecurrenceInterval);
        Assert.That(recurringResponse.IsSuccessStatusCode, Is.True, $"Schedule failed: {recurringResponse.StatusCode}");
        var recurringJobId = recurringResponse.Content!.JobId;

        var listAfterRecurring = await scheduleClient.List();
        var recurringEntry = listAfterRecurring.Content?.SingleOrDefault(e => e.JobId == recurringJobId);
        Assert.That(recurringEntry, Is.Not.Null, "Recurring notification did not appear in the list");
        Assert.That(recurringEntry!.RecurrenceInterval,
            Is.EqualTo(ScheduledNotificationService.MinRecurrenceInterval));

        // Fill the remaining slots with one-shots (the recurring notification already took 2).
        for (var i = currentWeight + 2; i < ScheduledNotificationService.MaxPendingPerTenant; i++)
        {
            var response = await scheduleClient.Schedule(NewOptions(), sendAt);
            Assert.That(response.IsSuccessStatusCode, Is.True,
                $"Schedule failed while filling the cap: {response.StatusCode}");
        }

        // One more is rejected -- proving the recurring notification's 2-slot weight was enforced.
        var overCapResponse = await scheduleClient.Schedule(NewOptions(), sendAt);
        AssertClientError(overCapResponse, OdinClientErrorCode.TooManyScheduledNotifications);
    }

    /// <summary>
    /// Both Schedule and Update require notification options; a null payload is rejected with a client
    /// error rather than silently scheduling/updating with no content.
    /// </summary>
    [Test]
    public async Task ScheduledNotification_RequiresOptions()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scheduleClient = ScheduleClient(owner);

        var sendAt = UnixTimeUtc.Now().AddHours(1);

        // Schedule with no options is rejected.
        var scheduleResponse = await scheduleClient.Schedule(null, sendAt);
        AssertClientError(scheduleResponse, OdinClientErrorCode.ArgumentError);

        // Schedule a real notification so there's a job id to attempt an update against.
        var validScheduleResponse = await scheduleClient.Schedule(NewOptions(), sendAt);
        Assert.That(validScheduleResponse.IsSuccessStatusCode, Is.True,
            $"Schedule failed: {validScheduleResponse.StatusCode}");
        var jobId = validScheduleResponse.Content!.JobId;

        // Update with no options is rejected too.
        var updateResponse = await scheduleClient.Update(jobId, null, sendAt);
        AssertClientError(updateResponse, OdinClientErrorCode.ArgumentError);

    }
}
