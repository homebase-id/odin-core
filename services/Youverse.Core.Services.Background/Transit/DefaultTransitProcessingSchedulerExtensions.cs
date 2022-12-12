using System;
using System.Configuration;
using Microsoft.Extensions.Configuration;
using Quartz;
using Quartz.Core;
using Serilog;

namespace Youverse.Core.Services.Workers.Transit
{
    public static class DefaultTransitProcessingSchedulerExtensions
    {
        /// <summary>
        /// Configure the outbox processing for Transit
        /// </summary>
        /// <param name="quartz"></param>
        /// <param name="backgroundJobStartDelaySeconds">Number of seconds to wait before starting the outbox processing during
        ///     system startup.  This is mainly useful long initiations and unit testing.</param>
        /// <param name="processOutboxIntervalSeconds"></param>
        public static void UseDefaultTransitOutboxSchedule(this IServiceCollectionQuartzConfigurator quartz, int backgroundJobStartDelaySeconds, int processOutboxIntervalSeconds)
        {
            var jobKey = new JobKey(nameof(StokeOutboxJob), "Transit");
            quartz.AddJob<StokeOutboxJob>(options => { options.WithIdentity(jobKey); });

            var triggerKey = new TriggerKey(jobKey.Name + "-trigger");
            quartz.AddTrigger(config =>
            {
                config.ForJob(jobKey);
                config.WithIdentity(triggerKey);

                config.WithSimpleSchedule(schedule => schedule
                    .RepeatForever()
                    .WithInterval(TimeSpan.FromSeconds(processOutboxIntervalSeconds))
                    .WithMisfireHandlingInstructionNextWithRemainingCount());

                config.StartAt(DateTimeOffset.UtcNow.Add(TimeSpan.FromSeconds(backgroundJobStartDelaySeconds)));
            });
            
            Log.Information($"Started Quartz Transit outbox Schedule with interval of {processOutboxIntervalSeconds} seconds");
        }
    }
}