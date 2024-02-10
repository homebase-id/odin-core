using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Services.Certificate;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Quartz;
using Odin.Core.Services.Registry;
using Quartz;

namespace Odin.Core.Services.Background.Certificate;

public class EnsureIdentityHasValidCertificateScheduler(OdinConfiguration odinConfig) : AbstractJobScheduler
{
    public override string JobType => "CertificateRenewal";

    /// <summary>
    /// Watches for certificates that need renewal; starts the process when required
    /// </summary>
    public override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .WithSimpleSchedule(schedule => schedule
                    .RepeatForever()
                    .WithInterval(TimeSpan.FromSeconds(odinConfig.Quartz.EnsureCertificateProcessorIntervalSeconds))
                    .WithMisfireHandlingInstructionNextWithRemainingCount())
                .StartAt(DateTimeOffset.UtcNow.Add(
                    TimeSpan.FromSeconds(odinConfig.Quartz.BackgroundJobStartDelaySeconds)))
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }
}

/// <summary>
/// Looks for certificates that require renewal and queues their renewal
/// </summary>
[DisallowConcurrentExecution]
public class EnsureIdentityHasValidCertificateJob(
    ICorrelationContext correlationContext,
    ILogger<EnsureIdentityHasValidCertificateJob> logger,
    IServiceProvider serviceProvider,
    IIdentityRegistry registry) : AbstractJob(correlationContext)
{
    protected sealed override async Task Run(IJobExecutionContext context)
    {
        logger.LogTrace("EnsureIdentityHasValidCertificateJob running...");

        var certificateServiceFactory = serviceProvider.GetRequiredService<ICertificateServiceFactory>();

        var tasks = new List<Task>();
        var identities = await registry.GetList();
        foreach (var identity in identities.Results)
        {
            var tenantContext = registry.CreateTenantContext(identity);
            var tc = certificateServiceFactory.Create(tenantContext.SslRoot);
            var task = tc.RenewIfAboutToExpire(identity);
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);
    }
}
