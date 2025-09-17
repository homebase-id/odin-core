using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Certificate;
using Odin.Services.Drives;
using Odin.Services.Security;
using Odin.Services.Security.Health;
using Odin.Services.Security.Health.RiskAnalyzer;

namespace Odin.Services.Background.BackgroundServices.Tenant;

// ReSharper disable once ClassNeverInstantiated.Global (well, it is done so by DI)
public sealed class SecurityHealthCheckBackgroundService(
    ILogger<SecurityHealthCheckBackgroundService> logger,
    ILifetimeScope lifetimeScope,
    ICertificateStore certificateStore,
    TenantContext tenantContext)
    : AbstractBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var domain = tenantContext.HostOdinId.DomainName;

            // Sanity: Make sure we have a certificate for the domain before processing the outbox.
            // Missing certificate can happen in rare, temporary, situations if the certificate has expired
            // or has not yet been created.
            if (await certificateStore.GetCertificateAsync(domain) == null)
            {
                logger.LogInformation("No certificate found for domain {domain}. Skipping outbox processing", domain);
                await SleepAsync(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            logger.LogDebug("SecurityHealthCheckBackgroundService: Tenant '{tenant}' is running", tenantContext.HostOdinId);

            await ValidateSecurityConfiguration();

            await SleepAsync(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private async Task ValidateSecurityConfiguration()
    {
        var odinContext = BuildOdinContext();

        var service = lifetimeScope.Resolve<OwnerSecurityHealthService>();

#if DEBUG
        await service.UpdateHealthCheck(odinContext);
#else
        var status = await service.GetRecoveryInfo(false, odinContext);
        var lastUpdated = status.RecoveryRisk.HealthLastChecked?.ToDateTime();
        if (lastUpdated == null || lastUpdated < DateTime.UtcNow.AddDays(-30))
        {
            await service.UpdateHealthCheck(odinContext);
        }
#endif

        //todo: based on how long we've last updated this, we can send a push notification
    }

    private IOdinContext BuildOdinContext()
    {
        var odinContext = new OdinContext
        {
            Tenant = tenantContext.HostOdinId,
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
                nameof(SecurityHealthCheckBackgroundService),
                new PermissionGroup(
                    new PermissionSet([PermissionKeys.UseTransitRead, PermissionKeys.ReadConnections]),
                    new List<DriveGrant>() { driveGrant }, null, null)
            }
        };

        odinContext.SetPermissionContext(new PermissionContext(permissionGroups, null, true));
        return odinContext;
    }
}