using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Cryptography.Crypto;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.JobManagement;

namespace Odin.Services.Membership.Connections.IcrKeyUpgrade;

/// <summary>
/// Used to handle upgrades to data for each tenant based on the version number
/// </summary>
public sealed class IcrKeyUpgradeScheduler(
    TenantContext tenantContext,
    ILogger<IcrKeyUpgradeScheduler> logger,
    IJobManager jobManager)
{
    private readonly IJobManager _jobManager = jobManager;

    public async Task EnsureScheduled(
        ClientAuthenticationToken token,
        IOdinContext odinContext,
        IcrKeyUpgradeJobData.JobTokenType jobTokenType
    )
    {
        if (!odinContext.PermissionsContext.HasAtLeastOnePermission(PermissionKeys.UseTransitRead, PermissionKeys.UseTransitWrite))
        {
            return;
        }

        if (string.IsNullOrEmpty(odinContext.Tenant.DomainName))
        {
            return;
        }

        var job = _jobManager.NewJob<IcrKeyUpgradeJob>();

        var (iv, encryptedToken) = AesCbc.Encrypt(token.ToPortableBytes(), tenantContext.TemporalEncryptionKey);

        job.Data = new IcrKeyUpgradeJobData()
        {
            TokenType = jobTokenType,
            Iv = iv,
            Tenant = odinContext.Tenant,
            EncryptedToken = encryptedToken
        };

        logger.LogInformation("Scheduling version upgrade job");
        await _jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            RunAt = DateTimeOffset.Now,
            MaxAttempts = 20,
            RetryDelay = TimeSpan.FromSeconds(3),
            OnSuccessDeleteAfter = TimeSpan.FromMinutes(0),
            OnFailureDeleteAfter = TimeSpan.FromMinutes(0),
        });
    }
}