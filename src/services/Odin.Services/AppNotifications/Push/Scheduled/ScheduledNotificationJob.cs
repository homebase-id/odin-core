#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Logging.Hostname;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Tenant.Container;

namespace Odin.Services.AppNotifications.Push.Scheduled;

/// <summary>
/// Background job that, when its scheduled time arrives, enqueues a (push) notification for a tenant
/// using the standard <see cref="PushNotificationService.EnqueueNotification"/> pipeline.  The job runs
/// in the system background, so it reconstructs a tenant-scoped, system <see cref="IOdinContext"/> that
/// is permitted to send push notifications (mirroring how the outbox processor pushes notifications).
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global (instantiated by DI via the job type registry)
public class ScheduledNotificationJob(
    IMultiTenantContainer tenantContainer,
    ILogger<ScheduledNotificationJob> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("b6a3c1e2-7f4d-4c8a-9d51-2e6f0a3b9c44");
    public override string JobType => JobTypeId.ToString();

    public ScheduledNotificationJobData Data { get; set; } = new();

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        try
        {
            if (Data.Tenant == null || !OdinId.IsValid(Data.Tenant.Value.DomainName))
            {
                logger.LogError("ScheduledNotificationJob received an empty/invalid tenant; aborting");
                return JobExecutionResult.Abort();
            }

            if (Data.Options == null)
            {
                logger.LogError("ScheduledNotificationJob for {tenant} has no notification options; aborting", Data.Tenant);
                return JobExecutionResult.Abort();
            }

            var tenant = Data.Tenant.Value;

            var tenantScope = tenantContainer.LookupTenantScope(tenant);
            if (tenantScope == null)
            {
                logger.LogError("ScheduledNotificationJob could not resolve tenant scope for {tenant}; aborting", tenant);
                return JobExecutionResult.Abort();
            }

            // Create a new lifetime scope for the tenant so db connections are isolated
            await using var scope = tenantScope.BeginLifetimeScope(
                $"{nameof(ScheduledNotificationJob)}:Run:{tenant}:{Guid.NewGuid()}");

            var stickyHostnameContext = scope.Resolve<IStickyHostname>();
            stickyHostnameContext.Hostname = $"{tenant}&";

            var odinContext = BuildOdinContext(tenant);

            var senderId = Data.SenderId ?? tenant;
            var pushNotificationService = scope.Resolve<PushNotificationService>();

            await pushNotificationService.EnqueueNotification(senderId, Data.Options, odinContext);

            logger.LogInformation(
                "ScheduledNotificationJob enqueued notification for tenant {tenant} (appId: {appId}, typeId: {typeId})",
                tenant, Data.Options.AppId, Data.Options.TypeId);
        }
        catch (Exception e)
        {
            logger.LogError(e, "{job} failed to run", nameof(ScheduledNotificationJob));
            return JobExecutionResult.Fail();
        }

        return JobExecutionResult.Success();
    }

    //

    /// <summary>
    /// If <see cref="ScheduledNotificationJobData.RecurrenceInterval"/> is set, keeps this job recurring
    /// by scheduling the next occurrence relative to the slot this one was meant to run for (rather than
    /// wall-clock now, to avoid cadence drift if the runner was delayed).  Called by the engine after a
    /// success, or after a failure that has exhausted all retry attempts.
    /// </summary>
    public override Task<DateTimeOffset?> OnCompletedAsync(JobCompletion completion)
    {
        if (Data.RecurrenceInterval is not { } interval)
        {
            return Task.FromResult<DateTimeOffset?>(null);
        }

        return Task.FromResult<DateTimeOffset?>(completion.ScheduledFor + TimeSpan.FromMilliseconds(interval));
    }

    //

    public override string SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(Data);
    }

    //

    public override void DeserializeJobData(string json)
    {
        Data = OdinSystemSerializer.DeserializeOrThrow<ScheduledNotificationJobData>(json);
    }

    //

    /// <summary>
    /// Builds a tenant-scoped system context that is allowed to send push notifications.  This follows the
    /// same approach the outbox processor uses when it sends notifications outside of an HTTP request.
    /// </summary>
    private static IOdinContext BuildOdinContext(OdinId tenant)
    {
        var odinContext = new OdinContext
        {
            Tenant = tenant,
            AuthTokenCreated = null,
            Caller = new CallerContext(
                odinId: (OdinId)"system.domain",
                masterKey: null,
                securityLevel: SecurityGroupType.System,
                circleIds: null,
                tokenType: ClientTokenType.Other)
        };

        var permissionGroups = new System.Collections.Generic.Dictionary<string, PermissionGroup>
        {
            {
                nameof(ScheduledNotificationJob),
                new PermissionGroup(
                    new PermissionSet([PermissionKeys.SendPushNotifications]),
                    null, null, null)
            }
        };

        odinContext.SetPermissionContext(new PermissionContext(permissionGroups, null, true));
        return odinContext;
    }
}
