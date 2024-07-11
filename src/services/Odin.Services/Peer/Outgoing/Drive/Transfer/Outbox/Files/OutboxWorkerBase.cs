using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

public abstract class OutboxWorkerBase(OutboxFileItem fileItem, FileSystemResolver fileSystemResolver, ILogger logger)
{
    protected OutboxFileItem FileItem => fileItem;

    protected async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> HandleOutboxProcessingException(IOdinContext odinContext, DatabaseConnection cn,
        OdinOutboxProcessingException e)
    {
        logger.LogDebug(e, "Failed to process outbox item for recipient: {recipient} " +
                           "with globalTransitId:{gtid}.  Transfer status was {transferStatus}",
            e.Recipient,
            e.GlobalTransitId,
            e.TransferStatus);


        switch (e.TransferStatus)
        {
            case LatestTransferStatus.RecipientIdentityReturnedAccessDenied:
            case LatestTransferStatus.UnknownServerError:
            case LatestTransferStatus.RecipientIdentityReturnedBadRequest:
            case LatestTransferStatus.SendingServerTooManyAttempts:
                return await HandleUnrecoverableTransferStatus(e, odinContext, cn);

            case LatestTransferStatus.RecipientIdentityReturnedServerError:
            case LatestTransferStatus.RecipientServerNotResponding:
            case LatestTransferStatus.SourceFileDoesNotAllowDistribution:
                return await HandleRecoverableTransferStatus(odinContext, cn, e);

            default:
                throw new OdinSystemException("Unhandled LatestTransferStatus");
        }
    }

    private async Task<(bool, UnixTimeUtc nextRunTime)> HandleRecoverableTransferStatus(IOdinContext odinContext, DatabaseConnection cn,
        OdinOutboxProcessingException e)
    {
        PerformanceCounter.IncrementCounter("Outbox Recoverable Error");

        var update = new UpdateTransferHistoryData()
        {
            IsInOutbox = true,
            LatestTransferStatus = e.TransferStatus,
            VersionTag = null
        };

        var nextRunTime = CalculateNextRunTime(e.TransferStatus);
        logger.LogDebug(e, "Marking Failure (popStamp:{marker})", fileItem.Marker);

        var fs = fileSystemResolver.ResolveFileSystem(fileItem.State.TransferInstructionSet.FileSystemType);
        await fs.Storage.UpdateTransferHistory(fileItem.File, fileItem.Recipient, update, odinContext, cn);

        return (false, nextRunTime);
    }

    private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e,
        IOdinContext odinContext,
        DatabaseConnection cn)
    {
        PerformanceCounter.IncrementCounter("Outbox Unrecoverable Error");

        logger.LogDebug(e, "Action: Removing from outbox and marking complete (popStamp:{marker})", fileItem.Marker);

        var update = new UpdateTransferHistoryData()
        {
            IsInOutbox = false,
            LatestTransferStatus = e.TransferStatus,
            VersionTag = null
        };

        var fs = fileSystemResolver.ResolveFileSystem(fileItem.State.TransferInstructionSet.FileSystemType);
        await fs.Storage.UpdateTransferHistory(fileItem.File, fileItem.Recipient, update, odinContext, cn);

        return (false, UnixTimeUtc.ZeroTime);
    }

    protected LatestTransferStatus MapPeerErrorResponseHttpStatus(ApiResponse<PeerTransferResponse> response)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return LatestTransferStatus.RecipientIdentityReturnedAccessDenied;
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return LatestTransferStatus.RecipientIdentityReturnedBadRequest;
        }

        return LatestTransferStatus.RecipientIdentityReturnedServerError;
    }

    protected UnixTimeUtc CalculateNextRunTime(LatestTransferStatus transferStatus)
    {
        switch (transferStatus)
        {
            case LatestTransferStatus.RecipientIdentityReturnedServerError:
            case LatestTransferStatus.RecipientServerNotResponding:
                return UnixTimeUtc.Now().AddSeconds(1);

            case LatestTransferStatus.SourceFileDoesNotAllowDistribution:
                return UnixTimeUtc.Now().AddSeconds(1);
            default:
                return UnixTimeUtc.Now().AddSeconds(1);
        }
    }
}