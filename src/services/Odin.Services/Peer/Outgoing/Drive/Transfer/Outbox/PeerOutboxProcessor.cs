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
        public async Task ProcessOutbox()
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
                        driveFileReaderWriter, concurrentFileManager)
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
            using (new UpgradeToPeerTransferSecurityContext(contextAccessor.GetCurrent()))
            {
                await pushNotificationService.ProcessBatch([item]);
            }

            //   await jobManager.Schedule<ProcessOutboxJob>(new ProcessOutboxSchedule(_contextAccessor.GetCurrent().Tenant,
            //                  nextRunTime));

            //TODO: Consider how to fall back for push notifications; i.e. do we really care?

            await peerOutbox.MarkComplete(item.Marker);
        }

        private async Task SendFileOutboxItem()
        {
            DriveStorageServiceBase storage;
            var driveAclAuthorizationService = new DriveAclAuthorizationService(contextAccessor,
                loggerFactory.CreateLogger<DriveAclAuthorizationService>());

            if (item.TransferInstructionSet.FileSystemType == FileSystemType.Standard)
            {
                storage = new StandardFileDriveStorageService(contextAccessor, loggerFactory, mediator, driveAclAuthorizationService, driveManager,
                    odinConfiguration, driveFileReaderWriter, concurrentFileManager);
            }
            else
            {
                storage = new CommentFileStorageService(contextAccessor, loggerFactory, mediator, driveAclAuthorizationService, driveManager,
                    odinConfiguration, driveFileReaderWriter, concurrentFileManager);
            }

            try
            {
                var versionTag = await SendOutboxFileItemAsync(item, storage);
                await UpdateTransferHistory(versionTag, null);
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
                await UpdateTransferHistory(versionTag: null, e.ProblemStatus);
                var nextRunTime = CalculateNextRunTime(item);

                await peerOutbox.MarkFailure(item.Marker, nextRunTime);

                await jobManager.Schedule<ProcessOutboxJob>(new ProcessOutboxSchedule(contextAccessor.GetCurrent().Tenant, nextRunTime));

                await mediator.Publish(new OutboxFileItemDeliveryFailedNotification(contextAccessor.GetCurrent())
                {
                    Recipient = item.Recipient,
                    File = item.File,
                    FileSystemType = item.TransferInstructionSet.FileSystemType,
                    ProblemStatus = e.ProblemStatus
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
                    ProblemStatus = LatestProblemStatus.UnknownServerError
                });
            }
        }

        private UnixTimeUtc CalculateNextRunTime(OutboxItem item)
        {
            //TODO: expand logic as needed
            // item.AddedTimestamp
            // item.AttemptCount > someValueInConfig
            switch (item.Type)
            {
                case OutboxItemType.File:
                    return UnixTimeUtc.Now().AddSeconds(5);
                // return UnixTimeUtc.Now().AddMinutes(5);

                case OutboxItemType.Reaction:
                    return UnixTimeUtc.Now().AddMinutes(5);

                case OutboxItemType.PushNotification:
                    return UnixTimeUtc.Now().AddMinutes(5);
            }

            return UnixTimeUtc.Now().AddMinutes(5);
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
                    ProblemStatus = LatestProblemStatus.SourceFileDoesNotAllowDistribution,
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
                //
                // var c1 = _contextAccessor.GetCurrent();
                //
                // var httpClient = new HttpClient();
                // httpClient.BaseAddress = new UriBuilder() { Scheme = "https", Host = "www.google.com" }.Uri;
                // var r = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/"));
                //
                // // response = await TrySendFile();
                // var c2 = _contextAccessor.GetCurrent();

                if (response.IsSuccessStatusCode)
                {
                    return versionTag;
                }

                throw new OdinOutboxProcessingException("Failed while sending the request")
                {
                    ProblemStatus = MapPeerResponseHttpStatus(response),
                    VersionTag = versionTag,
                    Recipient = recipient,
                    File = file
                };
            }
            catch (TryRetryException ex)
            {
                var e = ex.InnerException;
                var problemStatus = (e is TaskCanceledException or HttpRequestException or OperationCanceledException)
                    ? LatestProblemStatus.RecipientServerNotResponding
                    : LatestProblemStatus.UnknownServerError;

                throw new OdinOutboxProcessingException("Failed sending to recipient")
                {
                    ProblemStatus = problemStatus,
                    VersionTag = versionTag,
                    Recipient = recipient,
                    File = file
                };
            }
        }

        private async Task UpdateTransferHistory(Guid? versionTag, LatestProblemStatus? problemStatus)
        {
            DriveStorageServiceBase storage;
            var driveAclAuthorizationService = new DriveAclAuthorizationService(contextAccessor,
                loggerFactory.CreateLogger<DriveAclAuthorizationService>());

            if (item.TransferInstructionSet.FileSystemType == FileSystemType.Standard)
            {
                storage = new StandardFileDriveStorageService(contextAccessor, loggerFactory, mediator, driveAclAuthorizationService, driveManager,
                    odinConfiguration, driveFileReaderWriter,concurrentFileManager);
            }
            else
            {
                storage = new CommentFileStorageService(contextAccessor, loggerFactory, mediator, driveAclAuthorizationService, driveManager,
                    odinConfiguration, driveFileReaderWriter, concurrentFileManager);
            }

            await storage.UpdateTransferHistory(item.File, item.Recipient, versionTag, problemStatus);
        }

        private LatestProblemStatus MapPeerResponseHttpStatus(ApiResponse<PeerTransferResponse> response)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return LatestProblemStatus.RecipientIdentityReturnedAccessDenied;
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return LatestProblemStatus.RecipientIdentityReturnedBadRequest;
            }

            // if (response.StatusCode == HttpStatusCode.InternalServerError) // or HttpStatusCode.ServiceUnavailable
            {
                return LatestProblemStatus.RecipientIdentityReturnedServerError;
            }
        }
    }
}