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
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.DataSubscription;
using Odin.Services.DataSubscription.SendingHost;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.JobManagement;
using Odin.Services.Mediator;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Job;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

public class SendUnencryptedFeedFileOutboxWorkerAsync(
    OutboxFileItem fileItem,
    FileSystemResolver fileSystemResolver,
    ILogger<SendUnencryptedFeedFileOutboxWorkerAsync> logger,
    PeerOutbox peerOutbox,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory,
    IJobManager jobManager,
    IDriveAclAuthorizationService driveAcl
)

{
    public async Task Send(IOdinContext odinContext, DatabaseConnection cn, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogDebug("SendFeedItem -> Sending file: {file} to {recipient}", fileItem.File, fileItem.Recipient);

            var (versionTag, globalTransitId) = await HandleFeedItem(fileItem, odinContext, cn, cancellationToken);

            logger.LogDebug("SendFeedItem -> Successful transfer of {gtid} (version:{version}) to {recipient} - Action: " +
                            "Marking Complete (popStamp:{marker})",
                globalTransitId,
                versionTag,
                fileItem.Recipient,
                fileItem.Marker);

            await peerOutbox.MarkComplete(fileItem.Marker, cn);
        }
        catch (OdinFileReadException fileReadException)
        {
            logger.LogError(fileReadException, "SendFeedItem -> Failed sending file to {recipient}. " +
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

        var update = new UpdateTransferHistoryData()
        {
            IsInOutbox = true,
            LatestTransferStatus = e.TransferStatus,
            VersionTag = null
        };

        switch (e.TransferStatus)
        {
            case LatestTransferStatus.RecipientIdentityReturnedAccessDenied:
            case LatestTransferStatus.UnknownServerError:
            case LatestTransferStatus.RecipientIdentityReturnedBadRequest:
                logger.LogDebug(e, "Action: Removing from outbox and marking complete (popStamp:{marker})", fileItem.Marker);

                update.IsInOutbox = false;
                await peerOutbox.MarkComplete(fileItem.Marker, cn);
                break;

            case LatestTransferStatus.RecipientIdentityReturnedServerError:
            case LatestTransferStatus.RecipientServerNotResponding:
            case LatestTransferStatus.SourceFileDoesNotAllowDistribution:
                update.IsInOutbox = true;
                var nextRunTime = CalculateNextRunTime(e.TransferStatus);
                await jobManager.Schedule<ProcessOutboxJob>(new ProcessOutboxSchedule(odinContext.Tenant, nextRunTime));
                logger.LogDebug(e, "Scheduled re-run. NextRunTime (popStamp:{nextRunTime})", nextRunTime);

                logger.LogDebug(e, "Marking Failure (popStamp:{marker})", fileItem.Marker);
                await peerOutbox.MarkFailure(fileItem.Marker, nextRunTime, cn);
                break;

            default:
                logger.LogWarning(e, "Unhandled Transfer Status: {transferStatus}.  Action: Marking Complete", e.TransferStatus);
                await peerOutbox.MarkComplete(fileItem.Marker, cn);
                break;
        }
    }

    private async Task<(Guid versionTag, Guid globalTransitId)> HandleFeedItem(OutboxFileItem outboxFileItem, IOdinContext odinContext,
        DatabaseConnection cn, CancellationToken cancellationToken)
    {
        OdinId recipient = outboxFileItem.Recipient;
        var file = outboxFileItem.File;

        var distroItem = OdinSystemSerializer.Deserialize<FeedDistributionItem>(outboxFileItem.State.Data.ToStringFromUtf8Bytes());
        var fs = await fileSystemResolver.ResolveFileSystem(file, odinContext, cn);
        var header = await fs.Storage.GetServerFileHeader(file, odinContext, cn);

        if (header == null)
        {
            throw new OdinFileReadException($"File Source {file} file does not exist");
        }

        var versionTag = header.FileMetadata.VersionTag;
        var globalTransitId = header.FileMetadata.GlobalTransitId;

        var authorized = await driveAcl.IdentityHasPermission(recipient,
            header.ServerMetadata.AccessControlList, odinContext, cn);

        if (!authorized)
        {
            throw new OdinOutboxProcessingException("Failed sending to recipient")
            {
                TransferStatus = LatestTransferStatus.SourceFileDoesNotAllowDistribution,
                VersionTag = header.FileMetadata.VersionTag.GetValueOrDefault(),
                Recipient = recipient,
                GlobalTransitId = header.FileMetadata.GlobalTransitId,
                File = file
            };
        }

        try
        {
            ApiResponse<PeerTransferResponse> response;
            switch (distroItem.DriveNotificationType)
            {
                case DriveNotificationType.FileAdded:
                case DriveNotificationType.FileModified:
                    response = await SendFile(header, distroItem, recipient, cancellationToken);
                    break;

                case DriveNotificationType.FileDeleted:
                    response = await DeleteFile(header, recipient, distroItem.FileSystemType, cancellationToken);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (response.IsSuccessStatusCode)
            {
                return (versionTag.GetValueOrDefault(), globalTransitId.GetValueOrDefault());
            }

            throw new OdinOutboxProcessingException("Failed while sending the request")
            {
                TransferStatus = MapPeerErrorResponseHttpStatus(response),
                VersionTag = versionTag.GetValueOrDefault(),
                GlobalTransitId = globalTransitId,
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
                VersionTag = versionTag.GetValueOrDefault(),
                Recipient = recipient,
                GlobalTransitId = globalTransitId,
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

    private async Task<ApiResponse<PeerTransferResponse>> SendFile(ServerFileHeader header, FeedDistributionItem distroItem, OdinId recipient,
        CancellationToken cancellationToken)
    {
        var request = new UpdateFeedFileMetadataRequest()
        {
            FileId = new GlobalTransitIdFileIdentifier()
            {
                GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                TargetDrive = SystemDriveConstants.FeedDrive
            },
            UniqueId = header.FileMetadata.AppData.UniqueId,
            FileMetadata = header.FileMetadata,
            FeedDistroType = distroItem.FeedDistroType,
            EncryptedPayload = distroItem.EncryptedPayload
        };

        var client = odinHttpClientFactory.CreateClient<IFeedDistributorHttpClient>(recipient, fileSystemType: distroItem.FileSystemType);
        ApiResponse<PeerTransferResponse> httpResponse = null;

        await TryRetry.WithDelayAsync(
            odinConfiguration.Host.PeerOperationMaxAttempts,
            odinConfiguration.Host.PeerOperationDelayMs,
            cancellationToken,
            async () => { httpResponse = await client.SendFeedFileMetadata(request); });

        return httpResponse;
    }

    private async Task<ApiResponse<PeerTransferResponse>> DeleteFile(ServerFileHeader header, OdinId recipient, FileSystemType fileSystemType,
        CancellationToken cancellationToken)
    {
        var request = new DeleteFeedFileMetadataRequest()
        {
            FileId = new GlobalTransitIdFileIdentifier()
            {
                GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                TargetDrive = SystemDriveConstants.FeedDrive
            },
            UniqueId = header.FileMetadata.AppData.UniqueId,
        };

        var client = odinHttpClientFactory.CreateClient<IFeedDistributorHttpClient>(recipient, fileSystemType: fileSystemType);
        ApiResponse<PeerTransferResponse> httpResponse = null;

        await TryRetry.WithDelayAsync(
            odinConfiguration.Host.PeerOperationMaxAttempts,
            odinConfiguration.Host.PeerOperationDelayMs,
            cancellationToken,
            async () => { httpResponse = await client.DeleteFeedMetadata(request); });

        return httpResponse;
    }

}