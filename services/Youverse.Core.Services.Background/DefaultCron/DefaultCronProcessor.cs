using System;
using Quartz;
using Serilog;
using Youverse.Core.Services.Configuration;

namespace Youverse.Core.Services.Workers.DefaultCron
{
    public static class DefaultCronProcessor
    {
        public static void UseDefaultCronSchedule(this IServiceCollectionQuartzConfigurator quartz, YouverseConfiguration youverseConfig)
        {
            var jobKey = new JobKey(nameof(DefaultCronJob), "Cron");
            quartz.AddJob<DefaultCronJob>(options =>
            {
                options.WithIdentity(jobKey);
            });

            var triggerKey = new TriggerKey(jobKey.Name + "-trigger");

            quartz.AddTrigger(config =>
            {
                config.ForJob(jobKey);
                config.WithIdentity(triggerKey);

                config.WithSimpleSchedule(schedule => schedule
                    .RepeatForever()
                    .WithInterval(TimeSpan.FromSeconds(youverseConfig.Quartz.CronProcessingInterval))
                    .WithMisfireHandlingInstructionNextWithRemainingCount());

                config.StartAt(DateTimeOffset.UtcNow.Add(TimeSpan.FromSeconds(youverseConfig.Quartz.BackgroundJobStartDelaySeconds)));
            });

            Log.Information($"Started Quartz Transit outbox Schedule with interval of {youverseConfig.Quartz.CronProcessingInterval} seconds and batchsize of {youverseConfig.Quartz.CronBatchSize}");
        }
    }
}