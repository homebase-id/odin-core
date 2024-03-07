using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.Configuration;
using Odin.Services.Quartz;
using Odin.Services.Registry;
using Quartz;

namespace Odin.Services.Admin.Tenants.Jobs;
#nullable enable

public class ExportTenantScheduler(ILogger<ExportTenantScheduler> logger, string domain) : AbstractJobScheduler
{
    public sealed override string SchedulingKey { get; } = $"export-tenant:{domain}";

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        logger.LogDebug("Scheduling {Job}", SchedulingKey);

        jobBuilder
            .WithRetry(2, TimeSpan.FromSeconds(5))
            .WithRetention(TimeSpan.FromDays(2))
            .UsingJobData("domain", domain);

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .StartNow()
                .WithPriority(1)
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }
}

//

public class ExportTenantJob(
    ICorrelationContext correlationContext,
    ILogger<ExportTenantJob> logger,
    IIdentityRegistry identityRegistry,
    OdinConfiguration config) : AbstractJob(correlationContext)
{
    protected sealed override async Task Run(IJobExecutionContext context)
    {
        var domain = (string)context.JobDetail.JobDataMap["domain"];

        logger.LogDebug("Starting export tenant {domain}", domain);
        var sw = Stopwatch.StartNew();
        var targetPath = await identityRegistry.CopyRegistration(domain, config.Admin.ExportTargetPath);

        await SetJobResponseData(context, new ExportTenantData { TargetPath = targetPath });

        logger.LogDebug("Finished export tenant {domain} in {elapsed}s", domain, sw.ElapsedMilliseconds / 1000.0);
    }
}

//

public class ExportTenantData
{
    public string? TargetPath { get; set; }
}




