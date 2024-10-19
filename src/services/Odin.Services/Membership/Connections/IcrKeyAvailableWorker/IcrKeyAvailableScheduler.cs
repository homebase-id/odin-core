using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Time;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.JobManagement;

namespace Odin.Services.Membership.Connections.IcrKeyAvailableWorker;

/// <summary>
/// Used to handle upgrades to data for each tenant based on the version number
/// </summary>
public sealed class IcrKeyAvailableScheduler(
    TenantContext tenantContext,
    ILogger<IcrKeyAvailableScheduler> logger,
    IJobManager jobManager)
{
    private readonly IJobManager _jobManager = jobManager;

    private UnixTimeUtc _lastScheduledTime = UnixTimeUtc.ZeroTime;
    private const int MinWaitTimeBetweenRuns = 10; //TODO: config

    
    public async Task EnsureScheduled(
        ClientAuthenticationToken token,
        IOdinContext odinContext,
        IcrKeyAvailableJobData.JobTokenType jobTokenType
    )
    {
        // logger.LogDebug($"IcrKeyAvailableBackgroundService Last run: {_lastRunTime.seconds}");
        if (UnixTimeUtc.Now() < _lastScheduledTime.AddSeconds(MinWaitTimeBetweenRuns))
        {
            // logger.LogDebug("Not running IcrKeyAvailableBackgroundService Process");
            return;
        }
        
        if (!odinContext.PermissionsContext.HasAtLeastOnePermission(PermissionKeys.UseTransitRead, PermissionKeys.UseTransitWrite))
        {
            return;
        }

        if (string.IsNullOrEmpty(odinContext.Tenant.DomainName))
        {
            return;
        }

        var job = _jobManager.NewJob<IcrKeyAvailableJob>();

        var (iv, encryptedToken) = AesCbc.Encrypt(token.ToPortableBytes(), tenantContext.TemporalEncryptionKey);

        job.Data = new IcrKeyAvailableJobData()
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

        _lastScheduledTime = UnixTimeUtc.Now();
    }
}