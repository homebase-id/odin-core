#nullable enable

using System;
using System.Collections.Generic;
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
    /// Schedules <paramref name="options"/> to be enqueued/pushed at <paramref name="sendAt"/>.
    /// A <paramref name="sendAt"/> in the past results in the notification being sent as soon as possible.
    /// </summary>
    /// <returns>The id of the scheduled job, which can be used to cancel it.</returns>
    public async Task<Guid> ScheduleNotificationAsync(
        OdinId senderId,
        AppNotificationOptions? options,
        UnixTimeUtc sendAt,
        IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        if (options == null)
        {
            throw new OdinClientException("Notification options are required", OdinClientErrorCode.ArgumentError);
        }

        var tenant = odinContext.Tenant;

        var job = jobManager.NewJob<ScheduledNotificationJob>(tenantContext.DotYouRegistryId);
        job.Data = new ScheduledNotificationJobData
        {
            Tenant = tenant,
            SenderId = senderId,
            Options = options,
            SendAt = sendAt,
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
            "Scheduled notification job {jobId} for tenant {tenant} to run at {runAt} (appId: {appId}, typeId: {typeId})",
            jobId, tenant, runAt, options.AppId, options.TypeId);

        return jobId;
    }

    /// <summary>
    /// Cancels a previously scheduled notification.  Returns false if the job no longer exists
    /// (e.g. it already ran or was already cancelled).
    /// </summary>
    public async Task<bool> CancelScheduledNotificationAsync(Guid jobId, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);
        return await jobManager.DeleteJobByIdAsync(jobId);
    }

    /// <summary>
    /// Lists the tenant's pending/attempted scheduled notifications (i.e. not yet cleaned up by the job engine).
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
            if (data == null)
            {
                continue;
            }

            result.Add(new ScheduledNotificationSummary
            {
                JobId = record.id,
                Options = data.Options,
                SendAt = data.SendAt,
                State = ((JobState)record.state).ToString(),
            });
        }

        return result;
    }
}
