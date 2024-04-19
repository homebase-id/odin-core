using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    public class PeerOutboxProcessor(
        PeerOutbox peerOutbox,
        IOdinHttpClientFactory odinHttpClientFactory,
        OdinConfiguration odinConfiguration,
        ILogger<PeerOutboxProcessor> logger,
        IDriveFileSystem fileSystem,
        FileSystemResolver fileSystemResolver)
    {
        public async Task StartOutboxProcessingAsync(IOdinContext odinContext)
        {
            var item = await peerOutbox.GetNextItem();

            while (item != null)
            {
                _ = this.ProcessItem(item, odinContext);

                item = await peerOutbox.GetNextItem();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public async Task<List<OutboxProcessingResult>> ProcessItemsSync(IEnumerable<OutboxItem> items, IOdinContext odinContext)
        {
            var sendFileTasks = new List<Task<OutboxProcessingResult>>();
            var results = new List<OutboxProcessingResult>();

            sendFileTasks.AddRange(items.Select(i => ProcessItem(i, odinContext)));

            await Task.WhenAll(sendFileTasks);

            List<OutboxItem> filesForDeletion = new List<OutboxItem>();
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
                    var nextRun = UnixTimeUtc.Now().AddSeconds(-5);
                    peerOutbox.MarkFailure(sendResult.OutboxItem.Marker, nextRun);
                }
            });

            //TODO: optimization point; I need to see if this sort of deletion code is needed anymore; now that we have the transient temp drive
            foreach (var item in filesForDeletion)
            {
                var fs = fileSystemResolver.ResolveFileSystem(item.TransferInstructionSet.FileSystemType);
                await fs.Storage.HardDeleteLongTermFile(item.File, odinContext);
            }

            return results;
        }

        public async Task<OutboxProcessingResult> ProcessItem(OutboxItem item, IOdinContext odinContext)
        {
            //TODO: add benchmark
            logger.LogDebug("Processing outbox item type: {type}", item.Type);

            switch (item.Type)
            {
                case OutboxItemType.PushNotification:
                    return await SendPushNotification(item, odinContext);

                case OutboxItemType.File:
                    return await SendFileOutboxItem(item, odinContext);

                case OutboxItemType.Reaction:
                    return await SendReactionItem(item, odinContext);

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private async Task<OutboxProcessingResult> SendReactionItem(OutboxItem item, IOdinContext odinContext)
        {
            await Task.CompletedTask;
            throw new NotImplementedException("todo - support reactions in the outbox");
        }

        private async Task<OutboxProcessingResult> SendFileOutboxItem(OutboxItem item, IOdinContext odinContext)
        {
            var worker = new SendFileOutboxWorker(item,
                fileSystem,
                logger,
                peerOutbox,
                odinConfiguration,
                odinHttpClientFactory);

            return await worker.SendFileOutboxItem(odinContext);
        }

        private async Task<OutboxProcessingResult> SendPushNotification(OutboxItem item, IOdinContext odinContext)
        {
            await Task.CompletedTask;
            throw new NotImplementedException();
            await peerOutbox.MarkComplete(item.Marker);
            return null;
        }
    }

    public class SendFileOutboxWorker(
        OutboxItem item,
        IDriveFileSystem fileSystem,
        ILogger<PeerOutboxProcessor> logger,
        PeerOutbox peerOutbox,
        OdinConfiguration odinConfiguration,
        IOdinHttpClientFactory odinHttpClientFactory)
    {
        public async Task<OutboxProcessingResult> SendFileOutboxItem(IOdinContext odinContext)
        {
            try
            {
                var result = await SendOutboxFileItemAsync(item, odinContext);
                await peerOutbox.MarkComplete(item.Marker);
                return result;
            }
            catch (OdinOutboxProcessingException)
            {
                var nextRun = UnixTimeUtc.Now().AddSeconds(-5);
                await peerOutbox.MarkFailure(item.Marker, nextRun);
            }
            catch
            {
                await peerOutbox.MarkComplete(item.Marker);
            }

            return null;
        }

        private async Task<OutboxProcessingResult> SendOutboxFileItemAsync(OutboxItem outboxItem, IOdinContext odinContext)
        {
            OdinId recipient = outboxItem.Recipient;
            var file = outboxItem.File;
            var options = outboxItem.OriginalTransitOptions;

            var header = await fileSystem.Storage.GetServerFileHeader(outboxItem.File, odinContext);

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
            var transferInstructionSet = outboxItem.TransferInstructionSet;
            if (null == transferInstructionSet)
            {
                return new OutboxProcessingResult()
                {
                    File = file,
                    Recipient = recipient,
                    Timestamp = UnixTimeUtc.Now().milliseconds,
                    TransferResult = TransferResult.EncryptedTransferInstructionSetNotAvailable,
                    OutboxItem = outboxItem
                };
            }

            var shouldSendPayload = options.SendContents.HasFlag(SendContents.Payload);

            var decryptedClientAuthTokenBytes = outboxItem.EncryptedClientAuthToken;
            var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
            decryptedClientAuthTokenBytes.WriteZeros(); //never send the client auth token; even if encrypted

            if (options.UseAppNotification)
            {
                transferInstructionSet.AppNotificationOptions = options.AppNotificationOptions;
            }

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
                    OutboxItem = outboxItem
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
                    var p = await fileSystem.Storage.GetPayloadStream(file, payloadKey, null, odinContext);
                    var payloadStream = p.Stream;

                    var payload = new StreamPart(payloadStream, payloadKey, contentType, Enum.GetName(MultipartHostTransferParts.Payload));
                    additionalStreamParts.Add(payload);

                    foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
                    {
                        var (thumbStream, thumbHeader) =
                            await fileSystem.Storage.GetThumbnailPayloadStream(file, thumb.PixelWidth, thumb.PixelHeight, descriptor.Key, descriptor.Uid,
                                odinContext);

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
                    OutboxItem = outboxItem
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
                    OutboxItem = outboxItem
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
}