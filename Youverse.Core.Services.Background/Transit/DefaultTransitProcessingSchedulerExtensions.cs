using System;
using System.Configuration;
using Microsoft.Extensions.Configuration;
using Quartz;
using Quartz.Core;

namespace Youverse.Core.Services.Workers.Transit
{
    public static class DefaultTransitProcessingSchedulerExtensions
    {
        public static void UseDefaultTransitSchedule(this IServiceCollectionQuartzConfigurator quartz)
        {
            var jobKey = new JobKey(nameof(SendPayloadJob), "Transit");
            quartz.AddJob<SendPayloadJob>(options => { options.WithIdentity(jobKey); });
            
            var triggerKey = new TriggerKey(jobKey.Name + "-trigger");
            quartz.AddTrigger(config =>
            {
                config.ForJob(jobKey);
                config.WithIdentity(triggerKey);
                config.WithSimpleSchedule(schedule =>
                    schedule
                        .WithRepeatCount(100)
                        .WithInterval(TimeSpan.FromSeconds(30))
                        .WithMisfireHandlingInstructionNextWithRemainingCount());
            });
        }
    }
}