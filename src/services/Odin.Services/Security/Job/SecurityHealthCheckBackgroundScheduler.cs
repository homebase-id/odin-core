using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Background.BackgroundServices;
using Odin.Services.Base;
using Odin.Services.JobManagement;

namespace Odin.Services.Security.Job;

// ReSharper disable once ClassNeverInstantiated.Global (well, it is done so by DI)
public sealed class SecurityHealthCheckBackgroundScheduler(
    IJobManager jobManager,
    ILogger<SecurityHealthCheckBackgroundScheduler> logger,
    TenantContext tenantContext)
    : AbstractBackgroundService(logger)
{
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

#if !DEBUG
        await SleepAsync(TimeSpan.FromMinutes(1), stoppingToken);
#endif
        var job = jobManager.NewJob<SecurityHealthCheckJob>();
        job.Data = new SecurityHealthCheckJobData()
        {
            Tenant = tenantContext.HostOdinId
        };

        logger.LogInformation("Scheduling Security health check job");
        await jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            RunAt = DateTimeOffset.Now,
            MaxAttempts = 20,
            RetryDelay = TimeSpan.FromSeconds(3),
            OnSuccessDeleteAfter = TimeSpan.FromMinutes(0),
            OnFailureDeleteAfter = TimeSpan.FromMinutes(0),
        });
    }
}