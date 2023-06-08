using System;
using Odin.Core.Services.Configuration;
using Quartz;

namespace Odin.Core.Services.Background.Certificate;

public static class DefaultCertificateRenewalSchedulerExtensions
{
    /// <summary>
    /// Watches for certificates that need renewal; starts the process when required
    /// </summary>
    public static void UseDefaultCertificateRenewalSchedule(this IServiceCollectionQuartzConfigurator quartz,
        YouverseConfiguration odinConfig)
    {
        string group = "CertificateRenewal";

        var ensureCertificateJobKey = new JobKey(nameof(EnsureIdentityHasValidCertificateJob), group);
        quartz.AddJob<EnsureIdentityHasValidCertificateJob>(options =>
        {
            options.WithIdentity(ensureCertificateJobKey);
        });

        quartz.AddTrigger(config =>
        {
            config.ForJob(ensureCertificateJobKey);
            config.WithIdentity(new TriggerKey(ensureCertificateJobKey.Name + "-trigger"));

            config.WithSimpleSchedule(schedule => schedule
                .RepeatForever()
                .WithInterval(TimeSpan.FromSeconds(odinConfig.Quartz.EnsureCertificateProcessorIntervalSeconds))
                .WithMisfireHandlingInstructionNextWithRemainingCount());

            config.StartAt(DateTimeOffset.UtcNow.Add(TimeSpan.FromSeconds(odinConfig.Quartz.BackgroundJobStartDelaySeconds)));
        });
    }
}