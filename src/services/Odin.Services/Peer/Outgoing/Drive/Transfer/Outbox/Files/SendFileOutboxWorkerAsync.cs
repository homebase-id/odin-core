using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
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
using Odin.Services.Drives.FileSystem.Base;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

public class SendFileOutboxWorkerAsync(
    OutboxItem item,
    FileSystemResolver fileSystemResolver,
    // ILogger<PeerOutboxProcessor> logger,
    IPeerOutbox peerOutbox,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory,
    IMediator mediator
    // IJobManager jobManager
)
{
    public async Task<OutboxProcessingResult> Send(IOdinContext odinContext, DatabaseConnection cn)
    {
        var fs = fileSystemResolver.ResolveFileSystem(item.TransferInstructionSet.FileSystemType);

        try
        {
            var versionTag = await SendOutboxFileItemAsync(item, odinContext, cn);
 
            // Try to clean up the transient file
            if (item.IsTransientFile && !await peerOutbox.HasOutboxFileItem(item, cn))
            {
                await fs.Storage.HardDeleteLongTermFile(item.File, odinContext, cn);
            }

            var update = new UpdateTransferHistoryData()
            {
                IsInOutbox = false,
                IsReadByRecipient = false,
                LatestTransferStatus = LatestTransferStatus.Delivered,
                VersionTag = versionTag
            };

            await fs.Storage.UpdateTransferHistory(item.File, item.Recipient, update, odinContext, cn);
            await peerOutbox.MarkComplete(item.Marker, cn);

            await mediator.Publish(new OutboxFileItemDeliverySuccessNotification()
            {
                Recipient = item.Recipient,
                File = item.File,
                VersionTag = versionTag,
                OdinContext = odinContext,
                TransferStatus = LatestTransferStatus.Delivered,
                FileSystemType = item.TransferInstructionSet.FileSystemType
            });
        }
        catch (OdinOutboxProcessingException e)
        {
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
                    update.IsInOutbox = false;
                    await peerOutbox.MarkComplete(item.Marker, cn);
                    break;

                case LatestTransferStatus.RecipientIdentityReturnedServerError:
                case LatestTransferStatus.RecipientServerNotResponding:
                case LatestTransferStatus.RecipientDoesNotHavePermissionToSourceFile:
                case LatestTransferStatus.SourceFileDoesNotAllowDistribution:
                    update.IsInOutbox = true;
                    var nextRunTime = CalculateNextRunTime(e.TransferStatus);
                    // await jobManager.Schedule<ProcessOutboxJob>(new ProcessOutboxSchedule(contextAccessor.GetCurrent().Tenant, nextRunTime));
                    await peerOutbox.MarkFailure(item.Marker, nextRunTime, cn);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            await fs.Storage.UpdateTransferHistory(item.File, item.Recipient, update, odinContext, cn);

            await mediator.Publish(new OutboxFileItemDeliveryFailedNotification()
            {
                Recipient = item.Recipient,
                File = item.File,
                FileSystemType = item.TransferInstructionSet.FileSystemType,
                OdinContext = odinContext,
                TransferStatus = e.TransferStatus
            });
        }
        catch
        {
            await peerOutbox.MarkComplete(item.Marker, cn);
            await mediator.Publish(new OutboxFileItemDeliveryFailedNotification()
            {
                Recipient = item.Recipient,
                File = item.File,
                FileSystemType = item.TransferInstructionSet.FileSystemType,
                OdinContext = odinContext,
                TransferStatus = LatestTransferStatus.UnknownServerError
            });
        }

        return null;
    }

    private async Task<Guid> SendOutboxFileItemAsync(OutboxItem outboxItem, IOdinContext odinContext, DatabaseConnection cn)
    {
        OdinId recipient = outboxItem.Recipient;
        var file = outboxItem.File;
        var options = outboxItem.OriginalTransitOptions;

        var fileSystem = fileSystemResolver.ResolveFileSystem(item.TransferInstructionSet.FileSystemType);

        var header = await fileSystem.Storage.GetServerFileHeader(outboxItem.File, odinContext, cn);
        var versionTag = header.FileMetadata.VersionTag.GetValueOrDefault();

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
        var decryptedClientAuthTokenBytes = outboxItem.EncryptedClientAuthToken;
        var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
        decryptedClientAuthTokenBytes.WriteZeros(); //never send the client auth token; even if encrypted

        if (options.UseAppNotification)
        {
            outboxItem.TransferInstructionSet.AppNotificationOptions = options.AppNotificationOptions;
        }

        var redactedAcl = header.ServerMetadata.AccessControlList;
        redactedAcl?.OdinIdList?.Clear();
        outboxItem.TransferInstructionSet.OriginalAcl = redactedAcl;

        var transferInstructionSetBytes = OdinSystemSerializer.Serialize(outboxItem.TransferInstructionSet).ToUtf8ByteArray();
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
                return versionTag;
            }

            throw new OdinOutboxProcessingException("Failed while sending the request")
            {
                TransferStatus = MapPeerErrorResponseHttpStatus(response),
                VersionTag = versionTag,
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

        // if (response.StatusCode == HttpStatusCode.InternalServerError) // or HttpStatusCode.ServiceUnavailable
        {
            return LatestTransferStatus.RecipientIdentityReturnedServerError;
        }
    }

    private UnixTimeUtc CalculateNextRunTime(LatestTransferStatus transferStatus)
    {
        if (item.Type == OutboxItemType.File)
        {
            switch (transferStatus)
            {
                case LatestTransferStatus.RecipientIdentityReturnedServerError:
                case LatestTransferStatus.RecipientServerNotResponding:
                    return UnixTimeUtc.Now().AddSeconds(60);

                case LatestTransferStatus.RecipientDoesNotHavePermissionToSourceFile:
                case LatestTransferStatus.SourceFileDoesNotAllowDistribution:
                    return UnixTimeUtc.Now().AddMinutes(2);
            }
        }

        // item.AddedTimestamp
        // item.AttemptCount > someValueInConfig
        switch (item.Type)
        {
            case OutboxItemType.File:
                return UnixTimeUtc.Now().AddSeconds(5);

            case OutboxItemType.Reaction:
                return UnixTimeUtc.Now().AddMinutes(5);
        }

        return UnixTimeUtc.Now().AddSeconds(30);
    }
}