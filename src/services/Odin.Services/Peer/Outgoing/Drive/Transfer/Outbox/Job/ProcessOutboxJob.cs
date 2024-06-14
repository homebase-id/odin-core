using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.JobManagement;
using Quartz;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Job;
#nullable enable

public class ProcessOutboxSchedule(OdinId identity, UnixTimeUtc nextRunTime) : AbstractJobSchedule
{
    internal const string IdentityKey = "identity";

    public sealed override string SchedulingKey { get; } = nextRunTime.seconds.ToString();
    public override SchedulerGroup SchedulerGroup { get; } = SchedulerGroup.Default;

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        //TODO: add configuration
        jobBuilder
            .WithRetry(2, TimeSpan.FromMinutes(2))
            .WithRetention(TimeSpan.FromMinutes(1))
            .WithJobEvent<ProcessOutboxEvent>()
            .UsingJobData(IdentityKey, identity.ToString());

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .StartAt(DateTimeOffset.FromUnixTimeMilliseconds(nextRunTime.milliseconds))
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }
}

//

public class ProcessOutboxJob(
    ICorrelationContext correlationContext,
    ILogger<ProcessOutboxJob> logger,
    ISystemHttpClient systemHttpClient)
    : AbstractJob(correlationContext)
{
    protected sealed override async Task Run(IJobExecutionContext context)
    {
        logger.LogDebug("Process outbox running from ProcessOutboxJob");
        var jobData = context.JobDetail.JobDataMap;
        if (jobData.TryGetString(ProcessOutboxSchedule.IdentityKey, out var identity))
        {
            logger.LogDebug("ProcessOutboxJob running for  {identity}", identity);
            var svc = systemHttpClient.CreateHttps<IOutboxJobSystemHttpClient>((OdinId)identity);
            var response = await svc.ProcessOutboxAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new OdinSystemException($"Failed to run job for identity: {identity}");
            }

            // await SetJobResponseData(context, new DummyReponseData { Echo = echo });
        }
    }
}

//

public class ProcessOutboxEvent(ILogger<ProcessOutboxEvent> logger) : IJobEvent
{
    public Task Execute(IJobExecutionContext context, JobStatus status)
    {
        var jobData = context.JobDetail.JobDataMap;
        if (jobData.TryGetString("echo", out var echo))
        {
            logger.LogInformation("DummyEvent status:{status} echo:{echo}", status, echo);
        }

        return Task.CompletedTask;
    }
}