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
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.DataSubscription;
using Odin.Services.DataSubscription.SendingHost;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.JobManagement;
using Odin.Services.Mediator;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Job;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

public class SendUnencryptedFeedFileOutboxWorkerAsync(
    OutboxFileItem fileItem,
    FileSystemResolver fileSystemResolver,
    ILogger<SendUnencryptedFeedFileOutboxWorkerAsync> logger,
    IPeerOutbox peerOutbox,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory,
    IJobManager jobManager,
    IDriveAclAuthorizationService driveAcl
)

{
    public async Task Send(IOdinContext odinContext, DatabaseConnection cn, CancellationToken cancellationToken)
    {
        var fs = fileSystemResolver.ResolveFileSystem(fileItem.TransferInstructionSet.FileSystemType);

        try
        {
            logger.LogDebug("SendFeedItem -> Sending file: {file} to {recipient}", fileItem.File, fileItem.Recipient);

            var (versionTag, globalTransitId) = await SendFeedItem(fileItem, odinContext, cn, cancellationToken);

            logger.LogDebug("SendFeedItem -> Successful transfer of {gtid} to {recipient} - Action: " +
                            "Marking Complete (popStamp:{marker})",
                globalTransitId,
                fileItem.Recipient,
                fileItem.Marker);

            await peerOutbox.MarkComplete(fileItem.Marker, cn);
        }
        catch (OdinOutboxProcessingException e)
        {
            try
            {
                await HandleOutboxProcessingException(odinContext, cn, e, fs);
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

    private async Task HandleOutboxProcessingException(IOdinContext odinContext, DatabaseConnection cn, OdinOutboxProcessingException e, IDriveFileSystem fs)
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
                logger.LogWarning(e, "Unhandled Transfer Status: {transferStatus}", e.TransferStatus);
                break;
        }

        await fs.Storage.UpdateTransferHistory(fileItem.File, fileItem.Recipient, update, odinContext, cn);
    }

    private async Task<(Guid versionTag, Guid globalTransitId)> SendFeedItem(OutboxFileItem outboxFileItem, IOdinContext odinContext,
        DatabaseConnection cn,
        CancellationToken cancellationToken)
    {
        OdinId recipient = outboxFileItem.Recipient;
        var file = outboxFileItem.File;

        var distroItem = OdinSystemSerializer.Deserialize<FeedDistributionItem>(fileItem.RawValue.ToStringFromUtf8Bytes());

        if (distroItem.DriveNotificationType is DriveNotificationType.FileAdded or DriveNotificationType.FileModified)
        {
            bool success = await SendFile(file, distroItem, recipient, odinContext, cn);
        }

        if (distroItem.DriveNotificationType == DriveNotificationType.FileDeleted)
        {
            var success = await DeleteFile(file, distroItem.FileSystemType, recipient, odinContext, cn);
        }

        async Task<ApiResponse<PeerTransferResponse>> TrySendFile()
        {
            var client = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(recipient, clientAuthToken);
        }

        try
        {
            ApiResponse<PeerTransferResponse> response = null;

            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { response = await TrySendFile(); });

            if (response.IsSuccessStatusCode)
            {
                return (versionTag, globalTransitId.GetValueOrDefault());
            }

            throw new OdinOutboxProcessingException("Failed while sending the request")
            {
                TransferStatus = MapPeerErrorResponseHttpStatus(response),
                VersionTag = versionTag,
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
                VersionTag = versionTag,
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

    private async Task<bool> DeleteFile(InternalDriveFileId file, FileSystemType fileSystemType, OdinId recipient, IOdinContext odinContext,
        DatabaseConnection cn)
    {
        var fs = await fileSystemResolver.ResolveFileSystem(file, odinContext, cn);
        var header = await fs.Storage.GetServerFileHeader(file, odinContext, cn);

        if (null == header)
        {
            //TODO: need log more info here
            return false;
        }

        var authorized = await driveAcl.IdentityHasPermission(recipient,
            header.ServerMetadata.AccessControlList, odinContext, cn);

        if (!authorized)
        {
            //TODO: need more info here
            return false;
        }

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

        try
        {
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { httpResponse = await client.DeleteFeedMetadata(request); });
        }
        catch (TryRetryException e)
        {
            HandleTryRetryException(e);
            throw;
        }

        return IsSuccess(httpResponse);
    }

    private async Task<bool> SendFile(InternalDriveFileId file, FeedDistributionItem distroItem, OdinId recipient, IOdinContext odinContext,
        DatabaseConnection cn)
    {
        var fs = await fileSystemResolver.ResolveFileSystem(file, odinContext, cn);
        var header = await fs.Storage.GetServerFileHeader(file, odinContext, cn);

        if (null == header)
        {
            //TODO: need log more info here
            // need to ensure this is removed from the feed box
            return false;
        }

        var authorized = await driveAcl.IdentityHasPermission(recipient,
            header.ServerMetadata.AccessControlList, odinContext, cn);

        if (!authorized)
        {
            //TODO: need more info here
            return false;
        }

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
        ApiResponse<PeerTransferResponse> httpResponse = await client.SendFeedFileMetadata(request);
        return IsSuccess(httpResponse);
    }

    bool IsSuccess(ApiResponse<PeerTransferResponse> httpResponse)
    {
        if (null == httpResponse)
        {
            logger.LogError("httpResponse is null");
        }

        if (httpResponse?.IsSuccessStatusCode ?? false)
        {
            var transitResponse = httpResponse.Content;

            if (null == transitResponse)
            {
                logger.LogWarning("TransitResponse is missing the Code property; perhaps the identity's domain expired?");
                return false;
            }

            return transitResponse!.Code == PeerResponseCode.AcceptedDirectWrite || transitResponse!.Code == PeerResponseCode.AcceptedIntoInbox;
        }

        return false;
    }

    private void HandleTryRetryException(TryRetryException ex)
    {
        var e = ex.InnerException;
        if (e is TaskCanceledException || e is HttpRequestException || e is OperationCanceledException)
        {
            throw new OdinClientException("Failed while calling remote identity", e);
        }
    }
}