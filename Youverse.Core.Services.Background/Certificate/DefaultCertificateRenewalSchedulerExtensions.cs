using System;
using Quartz;

namespace Youverse.Core.Services.Workers.Certificate;

public static class DefaultCertificateRenewalSchedulerExtensions
{
    /// <summary>
    /// Configure the outbox processing for Transit
    /// </summary>
    /// <param name="quartz"></param>
    /// <param name="backgroundJobStartDelaySeconds">Number of seconds to wait before starting the outbox processing during
    /// system startup.  This is mainly useful long initiations and unit testing.</param>
    public static void UseDefaultCertificateRenewalSchedule(this IServiceCollectionQuartzConfigurator quartz, int backgroundJobStartDelaySeconds)
    {
        var jobKey = new JobKey(nameof(CheckCertificateStatusJob), "CertificateRenewal");
        quartz.AddJob<CheckCertificateStatusJob>(options => { options.WithIdentity(jobKey); });

        var triggerKey = new TriggerKey(jobKey.Name + "-trigger");
        quartz.AddTrigger(config =>
        {
            config.ForJob(jobKey);
            config.WithIdentity(triggerKey);

            config.WithSimpleSchedule(schedule => schedule
                .RepeatForever()
                .WithInterval(TimeSpan.FromSeconds(5))
                .WithMisfireHandlingInstructionNextWithRemainingCount());

            config.StartAt(DateTimeOffset.UtcNow.Add(TimeSpan.FromSeconds(backgroundJobStartDelaySeconds)));
        });
    }
}