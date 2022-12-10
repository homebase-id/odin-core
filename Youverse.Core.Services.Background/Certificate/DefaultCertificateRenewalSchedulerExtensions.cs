using System;
using Quartz;

namespace Youverse.Core.Services.Workers.Certificate;

public static class DefaultCertificateRenewalSchedulerExtensions
{
    /// <summary>
    /// Watches for certificates that need renewal; starts the process when required
    /// </summary>
    /// <param name="quartz"></param>
    /// <param name="backgroundJobStartDelaySeconds">Number of seconds to wait before starting the outbox processing during system startup.  This is mainly useful long initialization and unit testing.</param>
    /// <param name="certificateRenewalIntervalInSeconds"></param>
    /// <param name="processPendingCertificateOrderIntervalInSeconds"></param>
    public static void UseDefaultCertificateRenewalSchedule(this IServiceCollectionQuartzConfigurator quartz, int backgroundJobStartDelaySeconds,
        int certificateRenewalIntervalInSeconds,
        int processPendingCertificateOrderIntervalInSeconds)
    {
        string group = "CertificateRenewal";
        
        var ensureCertificateJobKey = new JobKey(nameof(EnsureIdentityHasValidCertificateJob), group);
        quartz.AddJob<EnsureIdentityHasValidCertificateJob>(options => { options.WithIdentity(ensureCertificateJobKey); });
        
        quartz.AddTrigger(config =>
        {
            config.ForJob(ensureCertificateJobKey);
            config.WithIdentity(new TriggerKey(ensureCertificateJobKey.Name + "-trigger"));

            config.WithSimpleSchedule(schedule => schedule
                .RepeatForever()
                .WithInterval(TimeSpan.FromSeconds(certificateRenewalIntervalInSeconds))
                .WithMisfireHandlingInstructionNextWithRemainingCount());

            config.StartAt(DateTimeOffset.UtcNow.Add(TimeSpan.FromSeconds(backgroundJobStartDelaySeconds)));
        });

        var processPendingCertificateOrdersJobKey = new JobKey(nameof(ProcessPendingCertificatesJob), group);
        quartz.AddJob<ProcessPendingCertificatesJob>(options => { options.WithIdentity(processPendingCertificateOrdersJobKey); });

        quartz.AddTrigger(config =>
        {
            config.ForJob(processPendingCertificateOrdersJobKey);
            config.WithIdentity(new TriggerKey($"{processPendingCertificateOrdersJobKey.Name}-trigger"));

            config.WithSimpleSchedule(schedule => schedule
                .RepeatForever()
                .WithInterval(TimeSpan.FromSeconds(processPendingCertificateOrderIntervalInSeconds))
                .WithMisfireHandlingInstructionNextWithRemainingCount());

            config.StartAt(DateTimeOffset.UtcNow.Add(TimeSpan.FromSeconds(backgroundJobStartDelaySeconds)));
        });
    }
}