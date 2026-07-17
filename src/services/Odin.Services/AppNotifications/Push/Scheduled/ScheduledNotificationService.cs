#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Time;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.JobManagement;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.AppNotifications.Push.Scheduled;

/// <summary>
/// Lets an owner or app schedule a (push) notification to be sent at a later time.  The notification is
/// persisted as a durable background job (<see cref="ScheduledNotificationJob"/>) and, when its time
/// arrives, is dispatched through the normal <see cref="PushNotificationService"/> pipeline.
/// </summary>
public class ScheduledNotificationService(
    IJobManager jobManager,
    TenantContext tenantContext,
    ILogger<ScheduledNotificationService> logger)
{
    /// <summary>
    /// Maximum number of scheduled notifications a single tenant may have pending at once.  This is a
    /// stopgap against one tenant flooding the shared system-wide job queue (see
    /// <see cref="ScheduledNotificationJob"/>); it is not tied to storage, but to keeping a single
    /// tenant's worst-case burst small enough that it can't meaningfully delay other job types sharing
    /// the same queue and priority tier.
    /// </summary>
    public const int MaxPendingPerTenant = 100;

    /// <summary>
    /// Minimum allowed <see cref="ScheduleNotificationRequest.RecurrenceInterval"/>, in milliseconds.
    /// Protects the shared system-wide job queue from a single recurring notification generating an
    /// unbounded rate of claims (see <see cref="ScheduledNotificationJob"/>); this is the real protection
    /// against sustained load, whereas <see cref="MaxPendingPerTenant"/> only bounds row count.
    /// </summary>
    public const long MinRecurrenceInterval = 5 * 60 * 1000; // 5 minutes

    /// <summary>
    /// Schedules <paramref name="options"/> to be enqueued/pushed at <paramref name="sendAt"/>.
    /// A <paramref name="sendAt"/> in the past results in the notification being sent as soon as possible.
    /// If <paramref name="recurrenceInterval"/> is set, the notification repeats every that many
    /// milliseconds after each occurrence's intended send time, instead of running once.
    /// </summary>
    /// <returns>The id of the scheduled job, which can be used to cancel or update it.</returns>
    public async Task<Guid> ScheduleNotificationAsync(
        OdinId senderId,
        AppNotificationOptions? options,
        UnixTimeUtc sendAt,
        long? recurrenceInterval,
        IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        if (options == null)
        {
            throw new OdinClientException("Notification options are required", OdinClientErrorCode.ArgumentError);
        }

        ValidateRecurrenceInterval(recurrenceInterval);

        // Recurring notifications never age out of the shared queue on their own (they cycle back to
        // Scheduled instead of expiring), so they count double against the tenant's own cap headroom.
        var thisWeight = recurrenceInterval != null ? 2 : 1;
        var pendingWeight = await CalculatePendingWeightAsync();
        if (pendingWeight + thisWeight > MaxPendingPerTenant)
        {
            throw new OdinClientException(
                $"Cannot schedule notification: this identity already has {pendingWeight} of {MaxPendingPerTenant} " +
                "allowed pending scheduled notification slots (a recurring notification counts as 2). Cancel " +
                "some existing scheduled notifications before scheduling more.",
                OdinClientErrorCode.TooManyScheduledNotifications);
        }

        var tenant = odinContext.Tenant;

        var job = jobManager.NewJob<ScheduledNotificationJob>(tenantContext.DotYouRegistryId);
        job.Data = new ScheduledNotificationJobData
        {
            Tenant = tenant,
            SenderId = senderId,
            Options = options,
            SendAt = sendAt,
            RecurrenceInterval = recurrenceInterval,
            ScheduledByAppId = odinContext.Caller.OdinClientContext?.AppId?.Value,
        };

        var runAt = DateTimeOffset.FromUnixTimeMilliseconds(sendAt.milliseconds);

        var jobId = await jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            RunAt = runAt,
            Priority = JobSchedule.HighPriority,
            MaxAttempts = 3,
            RetryDelay = TimeSpan.FromSeconds(30),
            OnSuccessDeleteAfter = TimeSpan.FromMinutes(1),
            OnFailureDeleteAfter = TimeSpan.FromDays(1),
        });

        logger.LogInformation(
            "Scheduled notification job {jobId} for tenant {tenant} to run at {runAt} " +
            "(appId: {appId}, typeId: {typeId}, recurring: {recurring})",
            jobId, tenant, runAt, options.AppId, options.TypeId, recurrenceInterval != null);

        return jobId;
    }

    /// <summary>
    /// Replaces the options, send time, and/or recurrence of a previously scheduled notification in
    /// place, keeping the same job id.  Resets the job to a fresh Scheduled state (clearing any prior
    /// attempt count or error), so it will run again even if it had already failed permanently.  Returns
    /// false if the job no longer exists, or belongs to another app (owner can update any app's schedule;
    /// an app can only update its own).
    /// </summary>
    public async Task<bool> UpdateScheduledNotificationAsync(
        Guid jobId,
        AppNotificationOptions? options,
        UnixTimeUtc sendAt,
        long? recurrenceInterval,
        IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        if (options == null)
        {
            throw new OdinClientException("Notification options are required", OdinClientErrorCode.ArgumentError);
        }

        ValidateRecurrenceInterval(recurrenceInterval);

        var job = await jobManager.GetJobAsync<ScheduledNotificationJob>(jobId);
        if (job == null || job.Data.Tenant != odinContext.Tenant || !CanAccess(job.Data, odinContext))
        {
            return false;
        }

        // Preserve identity/ownership fields from the original job; Options, SendAt, and
        // RecurrenceInterval are updatable.
        job.Data = new ScheduledNotificationJobData
        {
            Tenant = job.Data.Tenant,
            SenderId = job.Data.SenderId,
            Options = options,
            SendAt = sendAt,
            RecurrenceInterval = recurrenceInterval,
            ScheduledByAppId = job.Data.ScheduledByAppId,
        };

        var runAt = DateTimeOffset.FromUnixTimeMilliseconds(sendAt.milliseconds);
        var updated = await jobManager.RescheduleJobAsync(
            jobId, tenantContext.DotYouRegistryId, job.SerializeJobData(), runAt);

        if (updated)
        {
            logger.LogInformation(
                "Rescheduled notification job {jobId} for tenant {tenant} to run at {runAt} " +
                "(appId: {appId}, typeId: {typeId}, recurring: {recurring})",
                jobId, job.Data.Tenant, runAt, options.AppId, options.TypeId, recurrenceInterval != null);
        }

        return updated;
    }

    /// <summary>
    /// Cancels a previously scheduled notification.  Returns false if the job no longer exists,
    /// belongs to another app (owner can cancel any app's schedule; an app can only cancel its own),
    /// or was already cancelled/ran.
    /// </summary>
    public async Task<bool> CancelScheduledNotificationAsync(Guid jobId, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        var job = await jobManager.GetJobAsync<ScheduledNotificationJob>(jobId);
        if (job == null || job.Data.Tenant != odinContext.Tenant || !CanAccess(job.Data, odinContext))
        {
            return false;
        }

        return await jobManager.DeleteJobByIdAsync(jobId, tenantContext.DotYouRegistryId);
    }

    /// <summary>
    /// Lists the caller's pending/attempted scheduled notifications (i.e. not yet cleaned up by the job
    /// engine).  The owner sees every scheduled notification for the tenant; an app only sees the ones
    /// it scheduled itself.
    /// </summary>
    public async Task<List<ScheduledNotificationSummary>> ListScheduledNotificationsAsync(IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        var records = await jobManager.GetJobsByIdentityIdAsync(tenantContext.DotYouRegistryId);

        var result = new List<ScheduledNotificationSummary>();
        foreach (var record in records)
        {
            if (record.jobType != ScheduledNotificationJob.JobTypeId.ToString() || string.IsNullOrEmpty(record.jobData))
            {
                continue;
            }

            var data = OdinSystemSerializer.Deserialize<ScheduledNotificationJobData>(record.jobData);
            if (data == null || !CanAccess(data, odinContext))
            {
                continue;
            }

            result.Add(new ScheduledNotificationSummary
            {
                JobId = record.id,
                Options = data.Options,
                // record.nextRun is live and reflects the upcoming occurrence; data.SendAt is the
                // originally requested time and goes stale once a recurring notification fires once.
                SendAt = record.nextRun,
                State = ((JobState)record.state).ToString(),
                AttemptCount = record.runCount,
                MaxAttempts = record.maxAttempts,
                RecurrenceInterval = data.RecurrenceInterval,
            });
        }

        return result;
    }

    /// <summary>
    /// The owner can access any scheduled notification for the tenant; an app can only access the
    /// ones it scheduled itself.
    /// </summary>
    private static bool CanAccess(ScheduledNotificationJobData data, IOdinContext odinContext)
    {
        var callerAppId = odinContext.Caller.OdinClientContext?.AppId?.Value;
        return callerAppId == null || data.ScheduledByAppId == callerAppId;
    }

    /// <summary>
    /// Rejects a recurrence interval that's set but below <see cref="MinRecurrenceInterval"/>. A floor
    /// on the interval -- not the pending-count cap -- is what actually protects the shared job queue
    /// from a single recurring notification generating an unbounded claim rate.
    /// </summary>
    private static void ValidateRecurrenceInterval(long? recurrenceInterval)
    {
        if (recurrenceInterval is { } interval && interval < MinRecurrenceInterval)
        {
            throw new OdinClientException(
                $"Recurrence interval must be at least {MinRecurrenceInterval}ms ({MinRecurrenceInterval / 1000}s).",
                OdinClientErrorCode.ArgumentError);
        }
    }

    /// <summary>
    /// Weighs this tenant's scheduled notification jobs, across all apps and job states, for the purpose
    /// of enforcing <see cref="MaxPendingPerTenant"/>.  A recurring notification counts as 2 -- unlike a
    /// one-shot job it never ages out of the queue on its own, so it permanently occupies a slot.
    /// </summary>
    private async Task<int> CalculatePendingWeightAsync()
    {
        var records = await jobManager.GetJobsByIdentityIdAsync(tenantContext.DotYouRegistryId);

        var weight = 0;
        foreach (var record in records)
        {
            if (record.jobType != ScheduledNotificationJob.JobTypeId.ToString())
            {
                continue;
            }

            var isRecurring = false;
            if (!string.IsNullOrEmpty(record.jobData))
            {
                var data = OdinSystemSerializer.Deserialize<ScheduledNotificationJobData>(record.jobData);
                isRecurring = data?.RecurrenceInterval != null;
            }

            weight += isRecurring ? 2 : 1;
        }

        return weight;
    }
}
