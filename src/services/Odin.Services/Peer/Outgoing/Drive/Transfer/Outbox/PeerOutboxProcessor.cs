using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Comment;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;
using Odin.Services.JobManagement;
using Odin.Services.Mediator.Outbox;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Job;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    public class PeerOutboxProcessor(
        IOdinContextAccessor contextAccessor,
        PeerOutbox peerOutbox,
        IOdinHttpClientFactory odinHttpClientFactory,
        OdinConfiguration odinConfiguration,
        ILogger<PeerOutboxProcessor> logger,
        IMediator mediator,
        PushNotificationService pushNotificationService,
        IJobManager jobManager,
        ILoggerFactory loggerFactory,
        DriveManager driveManager,
        DriveFileReaderWriter driveFileReaderWriter,
        ConcurrentFileManager concurrentFileManager)
    {
        public async Task StartOutboxProcessing()
        {
            var item = await peerOutbox.GetNextItem();

            while (item != null)
            {
                var localContextAccessor = new ExplicitOdinContextAccessor(contextAccessor.GetCurrent());

                _ = new ProcessOutboxItemWorker(item, localContextAccessor, logger, peerOutbox,
                        mediator,
                        jobManager,
                        pushNotificationService,
                        odinConfiguration,
                        odinHttpClientFactory,
                        loggerFactory,
                        driveManager,
                        driveFileReaderWriter,
                        concurrentFileManager)
                    .ProcessOutboxItem();

                item = await peerOutbox.GetNextItem();
            }
        }
    }

    public class ProcessOutboxItemWorker(
        OutboxItem item,
        IOdinContextAccessor contextAccessor,
        ILogger<PeerOutboxProcessor> logger,
        PeerOutbox peerOutbox,
        IMediator mediator,
        IJobManager jobManager,
        PushNotificationService pushNotificationService,
        OdinConfiguration odinConfiguration,
        IOdinHttpClientFactory odinHttpClientFactory,
        ILoggerFactory loggerFactory,
        DriveManager driveManager,
        DriveFileReaderWriter driveFileReaderWriter,
        ConcurrentFileManager concurrentFileManager
    )
    {
        public async Task ProcessOutboxItem()
        {
            //TODO: add benchmark
            logger.LogDebug("Processing outbox item type: {type}", item.Type);

            switch (item.Type)
            {
                case OutboxItemType.PushNotification:
                    await SendPushNotification();
                    break;

                case OutboxItemType.File:
                    await SendFileOutboxItem();
                    break;

                case OutboxItemType.Reaction:
                    await SendReactionItem();
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private async Task SendReactionItem()
        {
            await Task.CompletedTask;
            throw new NotImplementedException("todo - support reactions in the outbox");
            //TODO:
            // await peerOutbox.MarkComplete(item.Marker);
        }

        private async Task SendPushNotification()
        {
            try
            {
                using (new UpgradeToPeerTransferSecurityContext(contextAccessor.GetCurrent()))
                {
                    await pushNotificationService.ProcessBatch([item]);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed sending push notification");
            }
            finally
            {
                await peerOutbox.MarkComplete(item.Marker);
            }
        }

        private async Task SendFileOutboxItem()
        {
            var storage = GetStorage();

            try
            {
                var versionTag = await SendOutboxFileItemAsync(item, storage);
                await storage.UpdateTransferHistory(item.File, item.Recipient, versionTag, LatestStatus.Processing);

                await peerOutbox.MarkComplete(item.Marker);
                await mediator.Publish(new OutboxFileItemDeliverySuccessNotification(contextAccessor.GetCurrent())
                {
                    Recipient = item.Recipient,
                    File = item.File,
                    VersionTag = versionTag,
                    FileSystemType = item.TransferInstructionSet.FileSystemType
                });
            }
            catch (OdinOutboxProcessingException e)
            {
                await storage.UpdateTransferHistory(item.File, item.Recipient, versionTag: null, e.Status);

                switch (e.Status)
                {
                    case LatestStatus.RecipientIdentityReturnedAccessDenied:
                    case LatestStatus.UnknownServerError:
                    case LatestStatus.RecipientIdentityReturnedBadRequest:
                        await peerOutbox.MarkComplete(item.Marker);
                        break;

                    case LatestStatus.RecipientIdentityReturnedServerError:
                    case LatestStatus.RecipientServerNotResponding:
                    case LatestStatus.RecipientDoesNotHavePermissionToSourceFile:
                    case LatestStatus.SourceFileDoesNotAllowDistribution:
                        var nextRunTime = CalculateNextRunTime(e.Status);
                        await jobManager.Schedule<ProcessOutboxJob>(new ProcessOutboxSchedule(contextAccessor.GetCurrent().Tenant, nextRunTime));
                        await peerOutbox.MarkFailure(item.Marker, nextRunTime);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }

                await mediator.Publish(new OutboxFileItemDeliveryFailedNotification(contextAccessor.GetCurrent())
                {
                    Recipient = item.Recipient,
                    File = item.File,
                    FileSystemType = item.TransferInstructionSet.FileSystemType,
                    Status = e.Status
                });
            }
            catch
            {
                await peerOutbox.MarkComplete(item.Marker);
                await mediator.Publish(new OutboxFileItemDeliveryFailedNotification(contextAccessor.GetCurrent())
                {
                    Recipient = item.Recipient,
                    File = item.File,
                    FileSystemType = item.TransferInstructionSet.FileSystemType,
                    Status = LatestStatus.UnknownServerError
                });
            }
        }

        private DriveStorageServiceBase GetStorage()
        {
            var driveAclAuthorizationService = new DriveAclAuthorizationService(contextAccessor,
                loggerFactory.CreateLogger<DriveAclAuthorizationService>());

            if (item.TransferInstructionSet.FileSystemType == FileSystemType.Standard)
            {
                return new StandardFileDriveStorageService(contextAccessor, loggerFactory, mediator, driveAclAuthorizationService, driveManager,
                    odinConfiguration, driveFileReaderWriter, concurrentFileManager);
            }

            return new CommentFileStorageService(contextAccessor, loggerFactory, mediator, driveAclAuthorizationService, driveManager,
                odinConfiguration, driveFileReaderWriter, concurrentFileManager);
        }

        private UnixTimeUtc CalculateNextRunTime(LatestStatus status)
        {
            if (item.Type == OutboxItemType.File)
            {
                switch (status)
                {
                    case LatestStatus.RecipientIdentityReturnedServerError:
                    case LatestStatus.RecipientServerNotResponding:
                        return UnixTimeUtc.Now().AddSeconds(60);

                    case LatestStatus.RecipientDoesNotHavePermissionToSourceFile:
                    case LatestStatus.SourceFileDoesNotAllowDistribution:
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

        private async Task<Guid> SendOutboxFileItemAsync(OutboxItem outboxItem, DriveStorageServiceBase storage)
        {
            OdinId recipient = outboxItem.Recipient;
            var file = outboxItem.File;
            var options = outboxItem.OriginalTransitOptions;

            var header = await storage.GetServerFileHeader(outboxItem.File);
            var versionTag = header.FileMetadata.VersionTag.GetValueOrDefault();

            // Enforce ACL at the last possible moment before shipping the file out of the identity; in case it changed
            // if (!await driveAclAuthorizationService.IdentityHasPermission(recipient, header.ServerMetadata.AccessControlList))
            // {
            //     throw new OdinOutboxProcessingException($"Recipient does not have permission to file ACL during send process")
            //     {
            //         ProblemStatus = LatestProblemStatus.RecipientDoesNotHavePermissionToSourceFile,
            //         VersionTag = versionTag,
            //         Recipient = recipient,
            //         File = file
            //     };
            // }

            if (header.ServerMetadata.AllowDistribution == false)
            {
                throw new OdinOutboxProcessingException("File does not allow distribution")
                {
                    Status = LatestStatus.SourceFileDoesNotAllowDistribution,
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

            var transferInstructionSetBytes = OdinSystemSerializer.Serialize(outboxItem.TransferInstructionSet).ToUtf8ByteArray();
            var transferKeyHeaderStream = new StreamPart(
                new MemoryStream(transferInstructionSetBytes),
                "transferInstructionSet.encrypted", "application/json",
                Enum.GetName(MultipartHostTransferParts.TransferKeyHeader));

            var sourceMetadata = header.FileMetadata;

            // redact the info by explicitly stating what we will keep
            // therefore, if a new attribute is added, it must be considered
            // if it should be sent to the recipient
            // DO NOT randomly add attributes here
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
                VersionTag = versionTag,
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
                    var p = await storage.GetPayloadStream(file, payloadKey, null);
                    var payloadStream = p.Stream;

                    var payload = new StreamPart(payloadStream, payloadKey, contentType, Enum.GetName(MultipartHostTransferParts.Payload));
                    additionalStreamParts.Add(payload);

                    foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
                    {
                        var (thumbStream, thumbHeader) =
                            await storage.GetThumbnailPayloadStream(file, thumb.PixelWidth, thumb.PixelHeight, descriptor.Key, descriptor.Uid);

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
                    Status = MapPeerResponseHttpStatus(response),
                    VersionTag = versionTag,
                    Recipient = recipient,
                    File = file
                };
            }
            catch (TryRetryException ex)
            {
                var e = ex.InnerException;
                var problemStatus = (e is TaskCanceledException or HttpRequestException or OperationCanceledException)
                    ? LatestStatus.RecipientServerNotResponding
                    : LatestStatus.UnknownServerError;

                throw new OdinOutboxProcessingException("Failed sending to recipient")
                {
                    Status = problemStatus,
                    VersionTag = versionTag,
                    Recipient = recipient,
                    File = file
                };
            }
        }

        private LatestStatus MapPeerResponseHttpStatus(ApiResponse<PeerTransferResponse> response)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return LatestStatus.RecipientIdentityReturnedAccessDenied;
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return LatestStatus.RecipientIdentityReturnedBadRequest;
            }

            // if (response.StatusCode == HttpStatusCode.InternalServerError) // or HttpStatusCode.ServiceUnavailable
            {
                return LatestStatus.RecipientIdentityReturnedServerError;
            }
        }
    }
}