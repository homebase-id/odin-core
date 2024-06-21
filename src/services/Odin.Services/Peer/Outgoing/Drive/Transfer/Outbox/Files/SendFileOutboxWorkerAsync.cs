using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.JobManagement;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Job;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

public class SendFileOutboxWorkerAsync(
    OutboxFileItem fileItem,
    FileSystemResolver fileSystemResolver,
    ILogger<SendFileOutboxWorkerAsync> logger,
    PeerOutbox peerOutbox,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory,
    IJobManager jobManager
)
{
    public async Task Send(IOdinContext odinContext, DatabaseConnection cn, CancellationToken cancellationToken)
    {
        var fs = fileSystemResolver.ResolveFileSystem(fileItem.TransferInstructionSet.FileSystemType);

        try
        {
            logger.LogDebug("Sending file: {file} to {recipient}", fileItem.File, fileItem.Recipient);

            var (versionTag, globalTransitId) = await SendOutboxFileItemAsync(fileItem, odinContext, cn, cancellationToken);

            var update = new UpdateTransferHistoryData()
            {
                IsInOutbox = false,
                IsReadByRecipient = false,
                LatestTransferStatus = LatestTransferStatus.Delivered,
                VersionTag = versionTag
            };

            await fs.Storage.UpdateTransferHistory(fileItem.File, fileItem.Recipient, update, odinContext, cn);

            logger.LogDebug("Successful transfer of {gtid} to {recipient} - Action: " +
                            "Marking Complete (popStamp:{marker})",
                globalTransitId,
                fileItem.Recipient,
                fileItem.Marker);

            await peerOutbox.MarkComplete(fileItem.Marker, cn);

            // Try to clean up the transient file
            if (fileItem.IsTransientFile && !await peerOutbox.HasOutboxFileItem(fileItem, cn))
            {
                logger.LogDebug("File was transient and all other outbox records sent; deleting");
                await fs.Storage.HardDeleteLongTermFile(fileItem.File, odinContext, cn);
            }
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
                logger.LogWarning(e, "Unhandled Transfer Status: {transferStatus}.  Action: Marking Complete", e.TransferStatus);
                await peerOutbox.MarkComplete(fileItem.Marker, cn);
                break;
        }

        await fs.Storage.UpdateTransferHistory(fileItem.File, fileItem.Recipient, update, odinContext, cn);
    }

    private async Task<(Guid versionTag, Guid globalTransitId)> SendOutboxFileItemAsync(OutboxFileItem outboxFileItem, IOdinContext odinContext,
        DatabaseConnection cn,
        CancellationToken cancellationToken)
    {
        OdinId recipient = outboxFileItem.Recipient;
        var file = outboxFileItem.File;
        var options = outboxFileItem.OriginalTransitOptions;

        var fileSystem = fileSystemResolver.ResolveFileSystem(fileItem.TransferInstructionSet.FileSystemType);

        var header = await fileSystem.Storage.GetServerFileHeader(outboxFileItem.File, odinContext, cn);
        var versionTag = header.FileMetadata.VersionTag.GetValueOrDefault();
        var globalTransitId = header.FileMetadata.GlobalTransitId;

        if (header.ServerMetadata.AllowDistribution == false)
        {
            throw new OdinOutboxProcessingException("File does not allow distribution")
            {
                TransferStatus = LatestTransferStatus.SourceFileDoesNotAllowDistribution,
                VersionTag = versionTag,
                Recipient = recipient,
                File = file
            };
        }

        var shouldSendPayload = options.SendContents.HasFlag(SendContents.Payload);
        var decryptedClientAuthTokenBytes = outboxFileItem.EncryptedClientAuthToken;
        var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
        decryptedClientAuthTokenBytes.WriteZeros(); //never send the client auth token; even if encrypted

        if (options.UseAppNotification)
        {
            outboxFileItem.TransferInstructionSet.AppNotificationOptions = options.AppNotificationOptions;
        }

        var redactedAcl = header.ServerMetadata.AccessControlList;
        redactedAcl?.OdinIdList?.Clear();
        outboxFileItem.TransferInstructionSet.OriginalAcl = redactedAcl;

        var transferInstructionSetBytes = OdinSystemSerializer.Serialize(outboxFileItem.TransferInstructionSet).ToUtf8ByteArray();
        var transferKeyHeaderStream = new StreamPart(
            new MemoryStream(transferInstructionSetBytes),
            "transferInstructionSet.encrypted", "application/json",
            Enum.GetName(MultipartHostTransferParts.TransferKeyHeader));

        var sourceMetadata = header.FileMetadata;

        //redact the info by explicitly stating what we will keep
        //therefore, if a new attribute is added, it must be considered if it should be sent to the recipient
        var redactedMetadata = new FileMetadata()
        {
            //TODO: here I am removing the file and drive id from the stream but we need
            // to resolve this by moving the file information to the server header
            File = InternalDriveFileId.Redacted(),
            Created = sourceMetadata.Created,
            Updated = sourceMetadata.Updated,
            AppData = sourceMetadata.AppData,
            IsEncrypted = sourceMetadata.IsEncrypted,
            GlobalTransitId = options.OverrideRemoteGlobalTransitId.GetValueOrDefault(sourceMetadata.GlobalTransitId.GetValueOrDefault()),
            ReactionPreview = sourceMetadata.ReactionPreview,
            SenderOdinId = sourceMetadata.SenderOdinId,
            ReferencedFile = sourceMetadata.ReferencedFile,
            VersionTag = sourceMetadata.VersionTag,
            Payloads = sourceMetadata.Payloads,
            FileState = sourceMetadata.FileState,
        };

        var json = OdinSystemSerializer.Serialize(redactedMetadata);
        var stream = new MemoryStream(json.ToUtf8ByteArray());
        var metaDataStream = new StreamPart(stream, "metadata.encrypted", "application/json", Enum.GetName(MultipartHostTransferParts.Metadata));

        var additionalStreamParts = new List<StreamPart>();

        if (shouldSendPayload)
        {
            foreach (var descriptor in redactedMetadata.Payloads ?? new List<PayloadDescriptor>())
            {
                var payloadKey = descriptor.Key;

                string contentType = "application/unknown";

                //TODO: consider what happens if the payload has been delete from disk
                var p = await fileSystem.Storage.GetPayloadStream(file, payloadKey, null, odinContext, cn);
                var payloadStream = p.Stream;

                var payload = new StreamPart(payloadStream, payloadKey, contentType, Enum.GetName(MultipartHostTransferParts.Payload));
                additionalStreamParts.Add(payload);

                foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
                {
                    var (thumbStream, thumbHeader) =
                        await fileSystem.Storage.GetThumbnailPayloadStream(file, thumb.PixelWidth, thumb.PixelHeight, descriptor.Key, descriptor.Uid,
                            odinContext, cn);

                    var thumbnailKey =
                        $"{payloadKey}" +
                        $"{DriveFileUtility.TransitThumbnailKeyDelimiter}" +
                        $"{thumb.PixelWidth}" +
                        $"{DriveFileUtility.TransitThumbnailKeyDelimiter}" +
                        $"{thumb.PixelHeight}";

                    additionalStreamParts.Add(new StreamPart(thumbStream, thumbnailKey, thumbHeader.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));
                }
            }
        }

        async Task<ApiResponse<PeerTransferResponse>> TrySendFile()
        {
            var client = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(recipient, clientAuthToken);
            var response = await client.SendHostToHost(transferKeyHeaderStream, metaDataStream, additionalStreamParts.ToArray());
            return response;
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
}