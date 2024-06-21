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
using Odin.Services.Drives.FileSystem.Base;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files.Old;

public class SendFileOutboxWorker(
    OutboxFileItem fileItem,
    FileSystemResolver fileSystemResolver,
    ILogger<PeerOutboxProcessor> logger,
    PeerOutbox peerOutbox,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory)
{
    public async Task<OutboxProcessingResult> Send(IOdinContext odinContext, bool tryDeleteTransient, DatabaseConnection cn)
    {
        try
        {
            var result = await SendOutboxFileItem(fileItem, odinContext, cn);
            logger.LogDebug("Send file item RecipientPeerResponseCode: {d}", result.RecipientPeerResponseCode);

            // Try to clean up the transient file
            if (result.TransferResult == TransferResult.Success)
            {
                if(tryDeleteTransient)
                {
                    if (fileItem.IsTransientFile && !await peerOutbox.HasOutboxFileItem(fileItem, cn))
                    {
                        var fs = fileSystemResolver.ResolveFileSystem(fileItem.TransferInstructionSet.FileSystemType);
                        await fs.Storage.HardDeleteLongTermFile(fileItem.File, odinContext, cn);
                    }
                }

                await peerOutbox.MarkComplete(fileItem.Marker, cn);

            }
            else
            {
                switch (result.TransferResult)
                {
                    case TransferResult.RecipientServerReturnedAccessDenied:
                    case TransferResult.UnknownError:
                    case TransferResult.FileDoesNotAllowDistribution:
                    case TransferResult.RecipientDoesNotHavePermissionToFileAcl:
                        await peerOutbox.MarkComplete(fileItem.Marker, cn);
                        break;

                    case TransferResult.RecipientServerNotResponding:
                    case TransferResult.RecipientServerError:
                        var nextRun = UnixTimeUtc.Now().AddSeconds(-5);
                        await peerOutbox.MarkFailure(fileItem.Marker, nextRun, cn);
                        break;
                }
            }

            return result;
        }
        catch (OdinOutboxProcessingException)
        {
            var nextRun = UnixTimeUtc.Now().AddSeconds(-5);
            await peerOutbox.MarkFailure(fileItem.Marker, nextRun, cn);
        }
        catch
        {
            await peerOutbox.MarkComplete(fileItem.Marker, cn);
        }

        return null;
    }

    private async Task<OutboxProcessingResult> SendOutboxFileItem(OutboxFileItem outboxFileItem, IOdinContext odinContext, DatabaseConnection cn)
    {
        OdinId recipient = outboxFileItem.Recipient;
        var file = outboxFileItem.File;
        var options = outboxFileItem.OriginalTransitOptions;

        var fileSystem = fileSystemResolver.ResolveFileSystem(fileItem.TransferInstructionSet.FileSystemType);

        var header = await fileSystem.Storage.GetServerFileHeader(outboxFileItem.File, odinContext, cn);

        // Enforce ACL at the last possible moment before shipping the file out of the identity; in case it changed
        // if (!await driveAclAuthorizationService.IdentityHasPermission(recipient, header.ServerMetadata.AccessControlList, odinContext))
        // {
        //     return new OutboxProcessingResult()
        //     {
        //         File = file,
        //         Recipient = recipient,
        //         Timestamp = UnixTimeUtc.Now().milliseconds,
        //         TransferResult = TransferResult.RecipientDoesNotHavePermissionToFileAcl,
        //         OutboxItem = outboxItem
        //     };
        // }

        //look up transfer key
        var transferInstructionSet = outboxFileItem.TransferInstructionSet;
        var shouldSendPayload = options.SendContents.HasFlag(SendContents.Payload);

        var decryptedClientAuthTokenBytes = outboxFileItem.EncryptedClientAuthToken;
        var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
        decryptedClientAuthTokenBytes.WriteZeros(); //never send the client auth token; even if encrypted

        if (options.UseAppNotification)
        {
            transferInstructionSet.AppNotificationOptions = options.AppNotificationOptions;
        }

        var redactedAcl = header.ServerMetadata.AccessControlList;
        redactedAcl?.OdinIdList?.Clear();
        transferInstructionSet.OriginalAcl = redactedAcl;

        var transferInstructionSetBytes = OdinSystemSerializer.Serialize(transferInstructionSet).ToUtf8ByteArray();
        var transferKeyHeaderStream = new StreamPart(
            new MemoryStream(transferInstructionSetBytes),
            "transferInstructionSet.encrypted", "application/json",
            Enum.GetName(MultipartHostTransferParts.TransferKeyHeader));

        if (header.ServerMetadata.AllowDistribution == false)
        {
            return new OutboxProcessingResult()
            {
                File = file,
                Recipient = recipient,
                Timestamp = UnixTimeUtc.Now().milliseconds,
                TransferResult = TransferResult.FileDoesNotAllowDistribution,
                OutboxFileItem = outboxFileItem
            };
        }

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
            PeerResponseCode peerCode = PeerResponseCode.Unknown;
            TransferResult transferResult = TransferResult.UnknownError;

            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { (peerCode, transferResult) = MapPeerResponseCode(await TrySendFile()); });

            return new OutboxProcessingResult()
            {
                File = file,
                Recipient = recipient,
                RecipientPeerResponseCode = peerCode,
                TransferResult = transferResult,
                Timestamp = UnixTimeUtc.Now().milliseconds,
                OutboxFileItem = outboxFileItem
            };
        }
        catch (TryRetryException ex)
        {
            var e = ex.InnerException;
            var tr = (e is TaskCanceledException or HttpRequestException or OperationCanceledException)
                ? TransferResult.RecipientServerNotResponding
                : TransferResult.UnknownError;

            return new OutboxProcessingResult()
            {
                File = file,
                Recipient = recipient,
                RecipientPeerResponseCode = null,
                TransferResult = tr,
                Timestamp = UnixTimeUtc.Now().milliseconds,
                OutboxFileItem = outboxFileItem
            };
        }
    }

    private (PeerResponseCode peerCode, TransferResult transferResult) MapPeerResponseCode(ApiResponse<PeerTransferResponse> response)
    {
        //TODO: needs more work to bring clarity to response code

        if (response.IsSuccessStatusCode)
        {
            return (response!.Content!.Code, TransferResult.Success);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (PeerResponseCode.Unknown, TransferResult.RecipientServerReturnedAccessDenied);
        }

        if (response.StatusCode == HttpStatusCode.InternalServerError)
        {
            return (PeerResponseCode.Unknown, TransferResult.RecipientServerError);
        }

        return (PeerResponseCode.Unknown, TransferResult.UnknownError);
    }
}