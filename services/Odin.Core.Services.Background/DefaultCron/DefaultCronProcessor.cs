using System;
using Odin.Core.Services.Configuration;
using Quartz;
using Serilog;

namespace Odin.Core.Services.Background.DefaultCron
{
    public static class DefaultCronProcessor
    {
        public static void UseDefaultCronSchedule(this IServiceCollectionQuartzConfigurator quartz, OdinConfiguration odinConfig)
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
                    .WithInterval(TimeSpan.FromSeconds(odinConfig.Quartz.CronProcessingInterval))
                    .WithMisfireHandlingInstructionNextWithRemainingCount());

                config.StartAt(DateTimeOffset.UtcNow.Add(TimeSpan.FromSeconds(odinConfig.Quartz.BackgroundJobStartDelaySeconds)));
            });

            Log.Information($"Started Quartz Transit outbox Schedule with interval of {odinConfig.Quartz.CronProcessingInterval} seconds and batchsize of {odinConfig.Quartz.CronBatchSize}");
        }
    }
}