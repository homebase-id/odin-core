using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.JobManagement;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Job;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

public class SendReadReceiptOutboxWorker(
    OutboxFileItem fileItem,
    ILogger<SendReadReceiptOutboxWorker> logger,
    PeerOutbox peerOutbox,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory,
    IJobManager jobManager
)
{
    public async Task Send(IOdinContext odinContext, DatabaseConnection cn, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogDebug("SendReadReceipt -> Sending request for file: {file} to {recipient}", fileItem.File, fileItem.Recipient);

            var globalTransitId = await HandleRequest(fileItem, cancellationToken);

            logger.LogDebug("SendReadReceipt -> Success for gtid {gtid} (version:{version}) to {recipient} - Action: " +
                            "Marking Complete (popStamp:{marker})",
                globalTransitId,
                "no version info",
                fileItem.Recipient,
                fileItem.Marker);

            await peerOutbox.MarkComplete(fileItem.Marker, cn);
        }
        catch (OdinFileReadException fileReadException)
        {
            logger.LogError(fileReadException, "SendDeleteFileRequest -> Failed sending file to {recipient}. " +
                                               "Action: Marking Complete (popStamp:{marker})",
                fileItem.Recipient,
                fileItem.Marker);
            await peerOutbox.MarkComplete(fileItem.Marker, cn);
        }
        catch (OdinOutboxProcessingException e)
        {
            try
            {
                await HandleOutboxProcessingException(odinContext, cn, e);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Error while handling the outbox processing exception " +
                                           "for file: {file} and recipient: {recipient} with version: " +
                                           "{version} and status: {status}",
                    e.File,
                    e.Recipient,
                    e.TransferStatus,
                    e.VersionTag);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            var nextRun = UnixTimeUtc.Now().AddSeconds(2);
            await peerOutbox.MarkFailure(fileItem.Marker, nextRun, cn);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unhandled error occured while sending file");
            await peerOutbox.MarkComplete(fileItem.Marker, cn);
        }
    }

    private async Task HandleOutboxProcessingException(IOdinContext odinContext, DatabaseConnection cn, OdinOutboxProcessingException e)
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
                logger.LogDebug(e, "Action: Removing from outbox and marking complete (popStamp:{marker})", fileItem.Marker);
                await peerOutbox.MarkComplete(fileItem.Marker, cn);
                break;

            case LatestTransferStatus.RecipientIdentityReturnedServerError:
            case LatestTransferStatus.RecipientServerNotResponding:
                var nextRunTime = CalculateNextRunTime(e.TransferStatus);
                await jobManager.Schedule<ProcessOutboxJob>(new ProcessOutboxSchedule(odinContext.Tenant, nextRunTime));
                logger.LogDebug(e, "Scheduled re-run. NextRunTime (popStamp:{nextRunTime})", nextRunTime);

                await peerOutbox.MarkFailure(fileItem.Marker, nextRunTime, cn);
                logger.LogDebug(e, "Marking Failure (popStamp:{marker})", fileItem.Marker);
                break;

            default:
                logger.LogWarning(e, "Unhandled Transfer Status: {transferStatus}.  Action: Marking Complete", e.TransferStatus);
                await peerOutbox.MarkComplete(fileItem.Marker, cn);
                break;
        }
    }

    private async Task<Guid> HandleRequest(OutboxFileItem outboxItem, CancellationToken cancellationToken)
    {
        OdinId recipient = outboxItem.Recipient;
        var file = outboxItem.File;

        var request = OdinSystemSerializer.Deserialize<MarkFileAsReadRequest>(outboxItem.State.Data.ToStringFromUtf8Bytes());

        var decryptedClientAuthTokenBytes = outboxItem.State.EncryptedClientAuthToken;
        var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
        decryptedClientAuthTokenBytes.WriteZeros(); //never send the client auth token; even if encrypted

        async Task<ApiResponse<PeerTransferResponse>> TrySendFile()
        {
            var client = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(
                recipient,
                clientAuthToken,
                request.FileSystemType);

            return await client.MarkFileAsRead(request);
        }

        try
        {
            ApiResponse<PeerTransferResponse> response = null;

            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                cancellationToken,
                async () => { response = await TrySendFile(); });

            if (response.IsSuccessStatusCode)
            {
                return request.GlobalTransitIdFileIdentifier.GlobalTransitId;
            }

            throw new OdinOutboxProcessingException("Failed while sending the request")
            {
                TransferStatus = MapPeerErrorResponseHttpStatus(response),
                VersionTag = default,
                GlobalTransitId = request.GlobalTransitIdFileIdentifier.GlobalTransitId,
                Recipient = recipient,
                File = file
            };
        }
        catch (TryRetryException ex)
        {
            var e = ex.InnerException;
            var status = (e is TaskCanceledException or HttpRequestException or OperationCanceledException)
                ? LatestTransferStatus.RecipientServerNotResponding
                : LatestTransferStatus.UnknownServerError;

            throw new OdinOutboxProcessingException("Failed sending to recipient")
            {
                TransferStatus = status,
                VersionTag = default,
                GlobalTransitId = request.GlobalTransitIdFileIdentifier.GlobalTransitId,
                Recipient = recipient,
                File = file
            };
        }
    }

    private LatestTransferStatus MapPeerErrorResponseHttpStatus(ApiResponse<PeerTransferResponse> response)
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

    private UnixTimeUtc CalculateNextRunTime(LatestTransferStatus transferStatus)
    {
        if (fileItem.Type == OutboxItemType.File)
        {
            switch (transferStatus)
            {
                case LatestTransferStatus.RecipientIdentityReturnedServerError:
                case LatestTransferStatus.RecipientServerNotResponding:
                    return UnixTimeUtc.Now().AddSeconds(60);

                case LatestTransferStatus.SourceFileDoesNotAllowDistribution:
                    return UnixTimeUtc.Now().AddMinutes(2);
                default:
                    return UnixTimeUtc.Now().AddMinutes(10);
            }
        }

        return UnixTimeUtc.Now().AddSeconds(30);
    }
}