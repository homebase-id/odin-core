using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Core.Storage.SQLite.ServerDatabase;
using Odin.Core.Time;
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
            "Scheduling Quartz Transit outbox Schedule with interval of {CronProcessingInterval} seconds and batch size of {CronBatchSize}",
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

        using var cn = serverSystemStorage.CreateConnection();
        var batch = serverSystemStorage.JobQueue.Pop(cn, batchSize);
        var tasks = new List<Task<(CronRecord record, bool success)>>(batch.Select(ProcessRecord));
        serverSystemStorage.JobQueue.PopCommitList(cn, tasks.Where(t => t.Result.success).Select(t => t.Result.record.popStamp.GetValueOrDefault()).ToList());
        serverSystemStorage.JobQueue.PopCancelList(cn, tasks.Where(t => !t.Result.success).Select(t => t.Result.record.popStamp.GetValueOrDefault()).ToList());

        const int deadTimeoutSeconds = 60 * 60;
        var time = UnixTimeUtc.FromDateTime(DateTime.Now.Subtract(TimeSpan.FromSeconds(deadTimeoutSeconds)));
        serverSystemStorage.JobQueue.PopRecoverDead(cn, time);

        return Task.CompletedTask;
    }

    private async Task<(CronRecord record, bool success)> ProcessRecord(CronRecord record)
    {
        var success = false;

        if (record.type == (Int32)CronJobType.ReconcileInboxOutbox)
        {
            success = await ProcessInboxOutboxReconciliation(record);
        }

        return (record, success);
    }

    private async Task<bool> ProcessInboxOutboxReconciliation(CronRecord record)
    {
        //if it's been 30 seconds since the last time we ran this item
        var t = DateTime.Now - record.lastRun.ToDateTime();
        if (t.TotalSeconds > config.Job.InboxOutboxReconciliationDelaySeconds)
        {
            var identity = (OdinId)record.data.ToStringFromUtf8Bytes();
            var svc = systemHttpClient.CreateHttps<ICronHttpClient>(identity);
            try
            {
                var response = await svc.ReconcileInboxOutbox();
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException e)
            {
                logger.LogDebug("Error reconciling inbox/outbox: {identity}. Error: {error}", identity, e.Message);
            }
        }
        return false;
    }

}