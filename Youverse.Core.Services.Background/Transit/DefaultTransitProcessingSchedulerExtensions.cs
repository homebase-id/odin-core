using System;
using System.Configuration;
using Microsoft.Extensions.Configuration;
using Quartz;
using Quartz.Core;

namespace Youverse.Core.Services.Workers.Transit
{
    public static class DefaultTransitProcessingSchedulerExtensions
    {
        /// <summary>
        /// Configure the outbox processing for Transit
        /// </summary>
        /// <param name="quartz"></param>
        /// <param name="backgroundJobStartDelaySeconds">Number of seconds to wait before starting the outbox processing during
        /// system startup.  This is mainly useful long initiations and unit testing.</param>
        public static void UseDefaultTransitOutboxSchedule(this IServiceCollectionQuartzConfigurator quartz, int backgroundJobStartDelaySeconds)
        {
            var jobKey = new JobKey(nameof(StokeOutboxJob), "Transit");
            quartz.AddJob<StokeOutboxJob>(options => { options.WithIdentity(jobKey); });

            var triggerKey = new TriggerKey(jobKey.Name + "-trigger");
            quartz.AddTrigger(config =>
            {
                config.ForJob(jobKey);
                config.WithIdentity(triggerKey);

                config.WithSimpleSchedule(schedule => schedule
                    //.RepeatForever()
                    .WithRepeatCount(10)
                    .WithInterval(TimeSpan.FromSeconds(5))
                    .WithMisfireHandlingInstructionNextWithRemainingCount());

                config.StartAt(DateTimeOffset.UtcNow.Add(TimeSpan.FromSeconds(backgroundJobStartDelaySeconds)));
            });
        }
    }
}