using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
using Odin.Services.Registry;
using Quartz;

namespace Odin.Services.Background.Certificate;

public class EnsureIdentityHasValidCertificateSchedule(OdinConfiguration odinConfig) : AbstractJobSchedule
{
    public sealed override string SchedulingKey => "CertificateRenewal";
    public sealed override SchedulerGroup SchedulerGroup { get; } = SchedulerGroup.Default;

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
                    .WithInterval(TimeSpan.FromSeconds(odinConfig.Job.EnsureCertificateProcessorIntervalSeconds))
                    .WithMisfireHandlingInstructionNextWithRemainingCount())
                .StartAt(DateTimeOffset.UtcNow.Add(
                    TimeSpan.FromSeconds(odinConfig.Job.BackgroundJobStartDelaySeconds)))
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
