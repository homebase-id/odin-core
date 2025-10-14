using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Logging.Hostname;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Email;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Security.Email;
using Odin.Services.Security.Health;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Security.Job;

#nullable enable

public class SecurityHealthCheckJobData
{
    public OdinId Tenant { get; init; }
}

// ReSharper disable once ClassNeverInstantiated.Global (well, it is done so by DI)
public class SecurityHealthCheckJob(
    IMultiTenantContainer tenantContainer,
    ICertificateStore certificateStore,
    ILogger<SecurityHealthCheckJob> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("5a42cc65-d2ca-4d41-b741-b4168cab7211");
    public override string JobType => JobTypeId.ToString();

    public SecurityHealthCheckJobData Data { get; set; } = new();

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        try
        {
            if (!Data.Tenant.HasValue())
            {
                logger.LogError("Security health check job received empty tenant; aborting");
                return JobExecutionResult.Abort();
            }

            // Sanity: Make sure we have a certificate for the domain before processing the outbox.
            // Missing certificate can happen in rare, temporary, situations if the certificate has expired
            // or has not yet been created.
            if (await certificateStore.GetCertificateAsync(Data.Tenant) == null)
            {
                logger.LogInformation("No certificate found for domain {domain}. Waiting one minute", Data.Tenant);
                return JobExecutionResult.Reschedule(DateTimeOffset.UtcNow.AddMinutes(1));
            }

            var tenantScope = tenantContainer.LookupTenantScope(Data.Tenant!);
            if (tenantScope == null)
            {
                return JobExecutionResult.Abort();
            }

            // Create a new lifetime scope for the tenant so db connections are isolated
            await using var scope = tenantScope.BeginLifetimeScope($"{nameof(SecurityHealthCheckJob)}:Run:{Data.Tenant}:{Guid.NewGuid()}");

            var stickyHostnameContext = scope.Resolve<IStickyHostname>();
            stickyHostnameContext.Hostname = $"{Data.Tenant}&";

            var odinContext = BuildOdinContext(Data.Tenant);

            var recoveryInfo = await RunHealthCheck(scope, odinContext);
            if (null != recoveryInfo)
            {
                // notify the user of health check
                var recoveryNotifier = scope.Resolve<RecoveryNotifier>();
                await recoveryNotifier.NotifyUser(Data.Tenant, recoveryInfo, odinContext);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, $"{nameof(SecurityHealthCheckJob)} Job railed to run");
            return JobExecutionResult.Fail();
        }

        return JobExecutionResult.Reschedule(DateTimeOffset.UtcNow.AddDays(30));
    }

    public override string? CreateJobHash()
    {
        var text = JobType + Data.Tenant;
        return SHA256.HashData(text.ToUtf8ByteArray()).ToBase64();
    }

    //

    public override string SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(Data);
    }

    //

    public override void DeserializeJobData(string json)
    {
        Data = OdinSystemSerializer.DeserializeOrThrow<SecurityHealthCheckJobData>(json);
    }

    //

    private async Task<RecoveryInfo?> RunHealthCheck(ILifetimeScope lifetimeScope, IOdinContext odinContext)
    {
        var configService = lifetimeScope.Resolve<TenantConfigService>();
        var isConfigured = await configService.IsIdentityServerConfiguredAsync();
        if (!isConfigured)
        {
            logger.LogDebug("Running security health check for {tenant} but IdentityServer is not configured yet.", Data.Tenant);
            return null;
        }

        var driveManager = lifetimeScope.Resolve<IDriveManager>();
        var shardDrive = await driveManager.GetDriveAsync(SystemDriveConstants.ShardRecoveryDrive.Alias);
        if (null == shardDrive)
        {
            logger.LogDebug("{job} -> Sharding drive not yet configured (Tenant probably needs to upgrade)",
                nameof(SecurityHealthCheckJob));
            return null;
        }

        var service = lifetimeScope.Resolve<OwnerSecurityHealthService>();
        var result = await service.GetRecoveryInfo(live: true, odinContext);
        return result;
    }

    private IOdinContext BuildOdinContext(OdinId tenant)
    {
        var odinContext = new OdinContext
        {
            Tenant = tenant,
            AuthTokenCreated = null,
            Caller = new CallerContext(
                odinId: (OdinId)"system.domain",
                masterKey: null,
                securityLevel: SecurityGroupType.Owner,
                circleIds: null,
                tokenType: ClientTokenType.Other)
        };

        var targetDrive = SystemDriveConstants.ShardRecoveryDrive;
        var driveGrant = new DriveGrant()
        {
            DriveId = targetDrive.Alias,
            PermissionedDrive = new()
            {
                Drive = targetDrive,
                Permission = DrivePermission.Read
            },
            KeyStoreKeyEncryptedStorageKey = null
        };

        var permissionGroups = new Dictionary<string, PermissionGroup>()
        {
            {
                nameof(SecurityHealthCheckJob),
                new PermissionGroup(
                    new PermissionSet([PermissionKeys.UseTransitRead, PermissionKeys.ReadConnections]),
                    new List<DriveGrant>() { driveGrant }, null, null)
            }
        };

        odinContext.SetPermissionContext(new PermissionContext(permissionGroups, null, true));
        return odinContext;
    }
}