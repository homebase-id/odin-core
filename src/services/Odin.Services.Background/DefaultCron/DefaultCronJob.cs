using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Core.Storage.SQLite.ServerDatabase;
using Odin.Services.Background.FeedDistributionApp;
using Odin.Services.JobManagement;
using Quartz;

namespace Odin.Services.Background.DefaultCron;

public class DefaultCronSchedule(
    ILogger<DefaultCronSchedule> logger,
    OdinConfiguration odinConfig) : AbstractJobSchedule
{
    public sealed override string SchedulingKey { get; } = "DefaultCron";
    public sealed override SchedulerGroup SchedulerGroup { get; } = SchedulerGroup.Default;

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .WithSimpleSchedule(schedule => schedule
                    .RepeatForever()
                    .WithInterval(TimeSpan.FromSeconds(odinConfig.Job.CronProcessingInterval))
                    .WithMisfireHandlingInstructionNextWithRemainingCount())
                .StartAt(DateTimeOffset.UtcNow.Add(
                    TimeSpan.FromSeconds(odinConfig.Job.BackgroundJobStartDelaySeconds)))
        };

        logger.LogInformation(
            "Scheduling Quartz Transit outbox Schedule with interval of {CronProcessingInterval} seconds and batchsize of {CronBatchSize}",
            odinConfig.Job.CronProcessingInterval, odinConfig.Job.CronBatchSize);

        return Task.FromResult((jobBuilder, triggerBuilders));
    }
}

//

[DisallowConcurrentExecution]
public class DefaultCronJob(
    ICorrelationContext correlationContext,
    ILogger<DefaultCronJob> logger,
    ServerSystemStorage serverSystemStorage,
    OdinConfiguration config,
    ISystemHttpClient systemHttpClient) : AbstractJob(correlationContext)
{
    protected sealed override Task Run(IJobExecutionContext context)
    {
        logger.LogTrace("DefaultCronJob running...");

        var batchSize = config.Job.CronBatchSize;
        if (batchSize <= 0)
        {
            throw new OdinSystemException("Job:CronBatchSize must be greater than zero");
        }

        var batch = serverSystemStorage.JobQueue.Pop(batchSize);
        var tasks = new List<Task<(CronRecord record, bool success)>>(batch.Select(ProcessRecord));
        serverSystemStorage.JobQueue.PopCommitList(tasks.Where(t => t.Result.success).Select(t => t.Result.record.popStamp.GetValueOrDefault()).ToList());
        serverSystemStorage.JobQueue.PopCancelList(tasks.Where(t => !t.Result.success).Select(t => t.Result.record.popStamp.GetValueOrDefault()).ToList());

        return Task.CompletedTask;
    }

    private async Task<(CronRecord record, bool success)> ProcessRecord(CronRecord record)
    {
        var success = false;
        if (record.type == (Int32)CronJobType.PendingTransitTransfer)
        {
            var identity = (OdinId)record.data.ToStringFromUtf8Bytes();
            success = await ProcessPeerTransferOutbox(identity);
        }

        if (record.type == (Int32)CronJobType.FeedDistribution)
        {
            var job = new FeedDistributionJob(config, systemHttpClient);
            success = await job.Execute(record);
        }

        if (record.type == (Int32)CronJobType.PushNotification)
        {
            var identity = (OdinId)record.data.ToStringFromUtf8Bytes();
            success = await PushNotifications(identity);
        }

        return (record, success);
    }

    private async Task<bool> ProcessPeerTransferOutbox(OdinId identity)
    {
        var svc = systemHttpClient.CreateHttps<ICronHttpClient>(identity);
        var response = await svc.ProcessOutbox();
        return response.IsSuccessStatusCode;
    }

    private async Task<bool> PushNotifications(OdinId identity)
    {
        var svc = systemHttpClient.CreateHttps<ICronHttpClient>(identity);
        var response = await svc.ProcessPushNotifications();
        return response.IsSuccessStatusCode;
    }
}


