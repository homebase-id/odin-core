using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Util;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Quartz;

namespace Odin.Services.Peer.Outgoing.Jobs;
#nullable enable

internal static class Consts
{
    public const string OutboxJsonItemKey = "outboxItemJson";
}

public class OutboxProcessingJob(
    OdinId sender,
    TransitOutboxItem item,
    OdinConfiguration configuration,
    SchedulerGroup schedulerGroup = SchedulerGroup.Default) : AbstractJobSchedule
{
    public sealed override string SchedulingKey { get; } =
        ByteArrayUtil.ReduceSHA256Hash($"{sender}{item.File.DriveId}{item.File.FileId}{item.Recipient}").ToString();

    public override SchedulerGroup SchedulerGroup { get; } = schedulerGroup;

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        //TODO: read from odin-configuration
        jobBuilder
            .WithRetry(2, TimeSpan.FromSeconds(5))
            .WithRetention(TimeSpan.FromMinutes(1))
            .WithJobEvent<OutboxProcessingJobCompletedEvent>()
            .UsingJobData(Consts.OutboxJsonItemKey, OdinSystemSerializer.Serialize(item));

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create().StartNow()
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }
}

//
public class OutboxProcessingJobCompletedEvent(ILogger<OutboxProcessingJobCompletedEvent> logger, IPeerOutbox peerOutbox) : IJobEvent
{
    public async Task Execute(IJobExecutionContext context, JobStatus status)
    {
        var jobData = context.JobDetail.JobDataMap;
        if (jobData.TryGetString(Consts.OutboxJsonItemKey, out var json))
        {
            var item = OdinSystemSerializer.Deserialize<TransitOutboxItem>(json!);

            if (status == JobStatus.Failed)
            {
                await peerOutbox.MarkFailure(item!.Marker, TransferResult.UnknownError);
            }

            if (status == JobStatus.Completed)
            {
                await peerOutbox.MarkComplete(item!.Marker);
            }

            return;
        }

        throw new OdinSystemException("something went really badly");
    }
}

//

public class OutboxItemProcessorJob(ICorrelationContext correlationContext, ILogger<OutboxItemProcessorJob> logger) : AbstractJob(correlationContext)
{
    protected sealed override async Task Run(IJobExecutionContext context)
    {
        var jobData = context.JobDetail.JobDataMap;
        if (jobData.TryGetString(Consts.OutboxJsonItemKey, out var json) && json != null)
        {
            var item = OdinSystemSerializer.Deserialize<OutboxRecord>(json);

            // This TryRetry is meant to handle network blips; 
            // attempts * delay will be the max amount a thread pool slot is used by this job; so keep the delay and attempts very small
            // and these are intentionally hard-coded in v1
            await TryRetry.WithDelayAsync(
                attempts: 1,
                delay: TimeSpan.FromMilliseconds(100),
                CancellationToken.None,
                async () => { (peerCode, transferResult) = MapPeerResponseCode(await TrySendFile()); });

            await context.UpdateJobMap("peerCode", peerCode);
            await context.UpdateJobMap("transferResult", transferResult);

        }
    }

    private async Task<List<OutboxProcessingResult>> SendItem(IEnumerable<TransitOutboxItem> items)
    {
        var sendFileTasks = new List<Task<OutboxProcessingResult>>();
        var results = new List<OutboxProcessingResult>();

        await Task.WhenAll(sendFileTasks);

        List<TransitOutboxItem> filesForDeletion = new List<TransitOutboxItem>();
        sendFileTasks.ForEach(task =>
        {
            var sendResult = task.Result;
            results.Add(sendResult);

            if (sendResult.TransferResult == TransferResult.Success)
            {
                if (sendResult.OutboxItem.IsTransientFile)
                {
                    filesForDeletion.Add(sendResult.OutboxItem);
                }

                peerOutbox.MarkComplete(sendResult.OutboxItem.Marker);
            }
            else
            {
                peerOutbox.MarkFailure(sendResult.OutboxItem.Marker, sendResult.TransferResult);
            }
        });

        //TODO: optimization point; I need to see if this sort of deletion code is needed anymore; now that we have the transient temp drive
        foreach (var item in filesForDeletion)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(item.TransferInstructionSet.FileSystemType);
            await fs.Storage.HardDeleteLongTermFile(item.File);
        }

        return results;
    }
}