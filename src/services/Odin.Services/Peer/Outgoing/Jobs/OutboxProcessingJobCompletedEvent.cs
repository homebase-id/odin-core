#nullable enable
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Quartz;

namespace Odin.Services.Peer.Outgoing.Jobs;

public class OutboxProcessingJobCompletedEvent(ILogger<OutboxProcessingJobCompletedEvent> logger, PeerOutbox peerOutbox) : IJobEvent
{
    public async Task Execute(IJobExecutionContext context, JobStatus status)
    {
        var jobData = context.JobDetail.JobDataMap;
        if (jobData.TryGetString(Keys.OutboxJsonProcessingResultKey, out var resultJson))
        {
            var result = OdinSystemSerializer.Deserialize<OutboxProcessingResult>(resultJson!);

            if (null == result)
            {
                throw new OdinSystemException("OutboxProcessingResult is null");
            }

            var failed = status == JobStatus.Failed ||
                         (status == JobStatus.Completed && (
                             result.TransferResult != TransferResult.Success));

            if (status == JobStatus.Failed)
            {
                await peerOutbox.MarkFailure(result.OutboxItem.Marker, TransferResult.UnknownError);
                return;
            }

            if (status == JobStatus.Completed)
            {
                //TODO: optimization point; I need to see if this sort of deletion code is needed anymore; now
                //that we have the transient temp drive
                // if (result.OutboxItem.IsTransientFile)
                // {
                //     var fs = _fileSystemResolver.ResolveFileSystem(itemToDelete.TransferInstructionSet.FileSystemType);
                //     await fs.Storage.HardDeleteLongTermFile(item.File);
                // }

                if (result.TransferResult != TransferResult.Success)
                {
                }

                await peerOutbox.MarkComplete(result.OutboxItem.Marker);
                await context.SetJobResponseData(result);
            }

            return;
        }

        throw new OdinSystemException("something went really badly");
    }
}