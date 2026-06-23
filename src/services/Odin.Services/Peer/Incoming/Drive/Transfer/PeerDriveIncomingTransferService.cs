using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.AppNotifications.Push;
using Odin.Services.AppNotifications.SystemNotifications;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.DataSubscription;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Util;

namespace Odin.Services.Peer.Incoming.Drive.Transfer
{
    public class PeerDriveIncomingTransferService(
        ILogger<PeerDriveIncomingTransferService> logger,
        IDriveManager driveManager,
        IDriveFileSystem fileSystem,
        IMediator mediator,
        PushNotificationService pushNotificationService,
        PeerOutbox peerOutbox,
        IOdinHttpClientFactory odinHttpClientFactory,
        CircleNetworkService circleNetworkService,
        FileSystemResolver fileSystemResolver,
        OdinConfiguration odinConfiguration,
        TransitInboxBoxStorage transitInboxBoxStorage,
        FeedWriter feedWriter
    ) : PeerServiceBase(odinHttpClientFactory, circleNetworkService, fileSystemResolver, odinConfiguration)
    {
        private IncomingTransferStateItem _transferState;

        private readonly Dictionary<string, List<string>> _uploadedKeys = new(StringComparer.InvariantCultureIgnoreCase);

        public async Task InitializeIncomingTransfer(EncryptedRecipientTransferInstructionSet transferInstructionSet,
            FileMetadata metadata,
            IOdinContext odinContext)
        {
            var driveId = transferInstructionSet.TargetDrive.Alias;
            var canDirectWrite = await CanDirectWriteFile(driveId, metadata, transferInstructionSet, odinContext);

            // Notice here: we always create a new fileId when receiving a new file.
            var file = await fileSystem.Storage.CreateInternalFileId(driveId, odinContext);

            _transferState = new IncomingTransferStateItem(file, canDirectWrite, transferInstructionSet);
            await WriteInstructionsAndMetadataToStorage(file, canDirectWrite, metadata, transferInstructionSet, odinContext);
        }

        public async Task AcceptPayload(string key, string fileExtension, Stream data, IOdinContext odinContext)
        {
            _uploadedKeys.TryAdd(key, new List<string>());
            if (_transferState.IsDirectWrite)
                await fileSystem.Storage.WriteUploadStream(_transferState.File, fileExtension, data, odinContext);
            else
                // Inbox-routed: stream straight to long-term under the incoming fileId (no inbox folder).
                // Inbox processing finds the payload already in place (see StagingArea.LongTerm).
                await fileSystem.Storage.WritePayloadDirectlyToLongTerm(_transferState.File, fileExtension, data, odinContext);
        }

        public async Task AcceptThumbnail(string payloadKey, string thumbnailKey, string fileExtension, Stream data,
            IOdinContext odinContext)
        {
            if (!_uploadedKeys.TryGetValue(payloadKey, out var thumbnailKeys))
            {
                thumbnailKeys = new List<string>();
                _uploadedKeys.Add(payloadKey, thumbnailKeys);
            }

            thumbnailKeys.Add(thumbnailKey);
            _uploadedKeys[payloadKey] = thumbnailKeys;

            if (_transferState.IsDirectWrite)
                await fileSystem.Storage.WriteUploadStream(_transferState.File, fileExtension, data, odinContext);
            else
                // Inbox-routed: stream straight to long-term under the incoming fileId (no inbox folder).
                await fileSystem.Storage.WritePayloadDirectlyToLongTerm(_transferState.File, fileExtension, data, odinContext);
        }

        public async Task<PeerTransferResponse> FinalizeTransfer(FileMetadata fileMetadata, IOdinContext odinContext)
        {
            var shouldExpectPayload = !fileMetadata.PayloadsAreRemote;

            // if there are payloads in the descriptor, and they should have been sent
            if ((fileMetadata.Payloads?.Any() ?? false) && shouldExpectPayload)
            {
                foreach (var expectedPayload in fileMetadata.Payloads)
                {
                    var hasPayload = _uploadedKeys.TryGetValue(expectedPayload.Key, out var thumbnailKeys);
                    if (!hasPayload)
                    {
                        throw new OdinClientException("Not all payloads received");
                    }

                    foreach (var expectedThumbnail in expectedPayload.Thumbnails)
                    {
                        var thumbnailKey = expectedThumbnail.CreateTransitKey(expectedPayload.Key);
                        if (thumbnailKeys.All(k => k != thumbnailKey))
                        {
                            throw new OdinClientException("Not all payloads received");
                        }
                    }
                }
            }

            var responseCode = await FinalizeTransferInternal(_transferState, fileMetadata, odinContext);

            if (responseCode == PeerResponseCode.AcceptedDirectWrite || responseCode == PeerResponseCode.AcceptedIntoInbox)
            {
                //Feed hack (again)
                if (_transferState.TransferInstructionSet.TargetDrive == SystemDriveConstants.FeedDrive ||
                    _transferState.TransferInstructionSet.TargetDrive.Type == SystemDriveConstants.ChannelDriveType)
                {
                    //Note: we say new feed item here because comments are never pushed into the feed drive; so any
                    //item going into the feed is new content (i.e. post/image, etc.)
                    await mediator.Publish(new NewFeedItemReceived
                    {
                        FileSystemType = _transferState.TransferInstructionSet.FileSystemType,
                        Sender = odinContext.GetCallerOdinIdOrFail(),
                        OdinContext = odinContext,
                        GlobalTransitId = fileMetadata.ReferencedFile != null
                            ? fileMetadata.ReferencedFile.GlobalTransitId
                            : fileMetadata.GlobalTransitId.GetValueOrDefault(),
                    });
                }
                else
                {
                    var notificationOptions = _transferState.TransferInstructionSet.AppNotificationOptions;
                    if (null != notificationOptions)
                    {
                        if (notificationOptions.Recipients?.Any() ?? false)
                        {
                            var drive = await driveManager.GetDriveAsync(_transferState.TransferInstructionSet.TargetDrive.Alias);

                            foreach (var recipient in notificationOptions.Recipients.Without(odinContext.Tenant))
                            {
                                try
                                {
                                    await EnqueuePeerPushNotificationDistribution(recipient, notificationOptions, drive, odinContext);
                                }
                                catch (Exception e)
                                {
                                    logger.LogInformation(e, "Failed why enqueueing peer push notification for recipient ({r})", recipient);
                                }
                            }

                            // also send to me
                            if (notificationOptions.Recipients.Any(r => r == odinContext.Tenant))
                            {
                                var senderId = odinContext.GetCallerOdinIdOrFail();
                                var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);
                                await pushNotificationService.EnqueueNotification(senderId, notificationOptions, newContext);
                            }
                        }
                        else
                        {
                            var senderId = odinContext.GetCallerOdinIdOrFail();
                            var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);
                            await pushNotificationService.EnqueueNotification(senderId, notificationOptions, newContext);
                        }
                    }
                }

                return new PeerTransferResponse() { Code = responseCode };
            }

            throw new OdinSystemException("Unhandled Routing");
        }

        public async Task<PeerTransferResponse> AcceptDeleteLinkedFileRequestAsync(TargetDrive targetDrive, Guid globalTransitId,
            FileSystemType fileSystemType,
            IOdinContext odinContext)
        {
            var driveId = targetDrive.Alias;

            logger.LogDebug("[DeleteFlow] AcceptDeleteLinkedFileRequest -> caller:{caller} gtid:{gtid} targetDrive:{drive} driveId:{driveId} fileSystemType:{fst}",
                odinContext.Caller?.OdinId, globalTransitId, targetDrive, driveId, fileSystemType);

            //TODO: add checks if the sender can write comments if this is a comment
            await fileSystem.Storage.AssertCanWriteToDrive(driveId, odinContext);

            var drive = await driveManager.GetDriveAsync(driveId);
            if (fileSystemType == FileSystemType.Comment || drive.IsCollaborationDrive())
            {
                logger.LogDebug("[DeleteFlow] AcceptDeleteLinkedFileRequest -> direct soft-delete path (comment/collab) for gtid:{gtid} driveId:{driveId}",
                    globalTransitId, driveId);

                //Note: we need to check if the person deleting the comment is the original commenter or the owner
                var header = await fileSystem.Query.GetFileByGlobalTransitId(driveId, globalTransitId, odinContext);
                if (null == header)
                {
                    logger.LogWarning("[DeleteFlow] AcceptDeleteLinkedFileRequest -> no file found by gtid:{gtid} on driveId:{driveId} (direct path)",
                        globalTransitId, driveId);
                    //TODO: should this be a 404?
                    throw new OdinClientException("Invalid global transit Id");
                }

                header.AssertOriginalSender(odinContext.Caller.OdinId.GetValueOrDefault());

                await fileSystem.Storage.SoftDeleteLongTermFile(new InternalDriveFileId()
                    {
                        FileId = header.FileId,
                        DriveId = driveId
                    },
                    odinContext, null);

                logger.LogDebug("[DeleteFlow] AcceptDeleteLinkedFileRequest -> direct soft-delete complete for fileId:{fileId} gtid:{gtid}",
                    header.FileId, globalTransitId);

                return new PeerTransferResponse()
                {
                    Code = PeerResponseCode.AcceptedDirectWrite
                };
            }

            var item = new TransferInboxItem()
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = odinContext.GetCallerOdinIdOrFail(),

                InstructionType = TransferInstructionType.DeleteLinkedFile,
                DriveId = driveId,

                FileId = Guid.NewGuid(), //HACK: use random guid for the fileId UID constraint
                GlobalTransitId = globalTransitId,

                FileSystemType = fileSystemType,
            };

            await transitInboxBoxStorage.AddAsync(item);

            await mediator.Publish(new InboxItemReceivedNotification
            {
                TargetDrive = targetDrive,
                FileSystemType = fileSystemType,
                TransferFileType = TransferFileType.Normal,
            });

            logger.LogDebug("[DeleteFlow] AcceptDeleteLinkedFileRequest -> queued to inbox and published InboxItemReceivedNotification; sender:{sender} gtid:{gtid} driveId:{driveId} inboxItemId:{itemId}",
                item.Sender, globalTransitId, driveId, item.Id);

            return new PeerTransferResponse()
            {
                Code = PeerResponseCode.AcceptedIntoInbox
            };
        }

        public async Task<PeerTransferResponse> MarkFileAsReadAsync(TargetDrive targetDrive, Guid globalTransitId,
            FileSystemType fileSystemType,
            IOdinContext odinContext)
        {
            var driveId = targetDrive.Alias;
            await fileSystem.Storage.AssertCanWriteToDrive(driveId, odinContext);

            var item = new TransferInboxItem()
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = odinContext.GetCallerOdinIdOrFail(),
                InstructionType = TransferInstructionType.ReadReceipt,
                DriveId = driveId,
                TransferFileType = TransferFileType.ReadReceipt,
                FileId = Guid.NewGuid(), //HACK: use random guid for the fileId UID constraint since we can have multiple senders sending a read receipt for the same gtid
                GlobalTransitId = globalTransitId,
                FileSystemType = fileSystemType,
            };

            await transitInboxBoxStorage.AddAsync(item);

            await mediator.Publish(new InboxItemReceivedNotification
            {
                TargetDrive = targetDrive,
                FileSystemType = fileSystemType,
                TransferFileType = TransferFileType.ReadReceipt,
            });

            return new PeerTransferResponse()
            {
                Code = PeerResponseCode.AcceptedIntoInbox
            };
        }

        public async Task CleanupStagingFiles(List<PayloadDescriptor> descriptors, IOdinContext odinContext)
        {
            if (_transferState?.File != null && _transferState.IsDirectWrite)
            {
                // use the descriptors from the package as they would have been uploaded to the upload folder
                await fileSystem.Storage.CleanupUploadTemporaryFiles(_transferState.File, descriptors, odinContext);
            }
        }

        //

        private async Task<PeerResponseCode> FinalizeTransferInternal(IncomingTransferStateItem stateItem, FileMetadata fileMetadata,
            IOdinContext odinContext)
        {
            //S0001, S1000, S2000 - can the sender write the content to the target drive?
            await fileSystem.Storage.AssertCanWriteToDrive(stateItem.File.DriveId, odinContext);

            var directWriteSuccess = await TryDirectWriteFile(stateItem, fileMetadata, odinContext);

            if (directWriteSuccess)
            {
                return PeerResponseCode.AcceptedDirectWrite;
            }

            logger.LogDebug("TryDirectWrite failed for file ({file}) - routing to inbox.", stateItem.File);

            //S1220 - the instruction set and metadata travel on the inbox row (no inbox folder); payloads are
            // already in long-term storage under the incoming fileId.
            return await RouteToInboxAsync(stateItem, fileMetadata, odinContext);
        }

        private async Task WriteInstructionsAndMetadataToStorage(InternalDriveFileId file, bool isDirectWrite,
            FileMetadata fileMetadata,
            EncryptedRecipientTransferInstructionSet instructionSet,
            IOdinContext odinContext)
        {
            logger.LogDebug("Writing metadata for file {file} (isDirectWrite: {isDirectWrite})", file, isDirectWrite);

            // Inbox-routed transfers stage nothing on disk: the instruction set and metadata travel on the inbox
            // row (TransferInboxItem.TransferInstructionSet / .FileMetadata) and payloads stream straight to
            // long-term. Only the direct-write path still uses the upload staging folder.
            if (!isDirectWrite)
            {
                return;
            }

            await using var stream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());
            await fileSystem.Storage.WriteUploadStream(file, TenantPathManager.TransferInstructionSetExtension, stream, odinContext);

            var metadataStream = new MemoryStream(Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(fileMetadata)));
            await fileSystem.Storage.WriteUploadStream(file, TenantPathManager.MetadataExtension, metadataStream, odinContext);
        }

        private async Task<bool> TryDirectWriteFile(IncomingTransferStateItem stateItem, FileMetadata metadata, IOdinContext odinContext)
        {
            if (!await CanDirectWriteFile(stateItem.File.DriveId, metadata, stateItem.TransferInstructionSet, odinContext))
            {
                return false;
            }

            //TODO: check if any apps are online and we can snag the storage key

            PeerFileWriter writer = new PeerFileWriter(logger, FileSystemResolver, driveManager, feedWriter);
            var sender = odinContext.GetCallerOdinIdOrFail();
            var decryptedKeyHeader = DecryptKeyHeaderWithSharedSecret(stateItem.TransferInstructionSet.SharedSecretEncryptedKeyHeader,
                odinContext);
            var drive = await driveManager.GetDriveAsync(stateItem.File.DriveId);

            if (metadata.IsEncrypted == false)
            {
                //S1110 - Write to disk and send notifications
                await writer.HandleFile(stateItem.File, fileSystem, decryptedKeyHeader, sender, stateItem.TransferInstructionSet,
                    odinContext, sourceArea: stateItem.IsDirectWrite ? StagingArea.Upload : StagingArea.Inbox);

                return true;
            }

            //S1100
            if (metadata.IsEncrypted)
            {
                // Next determine if we can direct write the file
                var hasStorageKey = odinContext.PermissionsContext.TryGetDriveStorageKey(stateItem.File.DriveId, out _);

                //S1200
                if (hasStorageKey)
                {
                    //S1205
                    await writer.HandleFile(stateItem.File, fileSystem, decryptedKeyHeader, sender, stateItem.TransferInstructionSet,
                        odinContext, sourceArea: stateItem.IsDirectWrite ? StagingArea.Upload : StagingArea.Inbox);
                    return true;
                }

                logger.LogDebug("Caller can direct-write to drive [{drive}] but does not have storage " +
                                "key for encrypted file", stateItem.File.DriveId);

                //S2210 - comments cannot fall back to inbox
                if (stateItem.TransferInstructionSet.FileSystemType == FileSystemType.Comment)
                {
                    throw new OdinSecurityException("Sender cannot write the comment");
                }
            }

            return false;
        }

        private KeyHeader DecryptKeyHeaderWithSharedSecret(EncryptedKeyHeader sharedSecretEncryptedKeyHeader, IOdinContext odinContext)
        {
            var sharedSecret = odinContext.PermissionsContext.SharedSecretKey;
            var decryptedKeyHeader = sharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
            return decryptedKeyHeader;
        }

        /// <summary>
        /// Stores the file in the inbox, so it can be processed by the owner in a separate process
        /// </summary>
        private async Task<PeerResponseCode> RouteToInboxAsync(IncomingTransferStateItem stateItem, FileMetadata fileMetadata,
            IOdinContext odinContext)
        {
            var item = new TransferInboxItem()
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = odinContext.GetCallerOdinIdOrFail(),

                InstructionType = TransferInstructionType.SaveFile,
                DriveId = stateItem.File.DriveId,
                FileId = stateItem.File.FileId,
                TransferInstructionSet = stateItem.TransferInstructionSet,

                // The incoming metadata rides on the inbox row instead of an inbox-folder .metadata file, so
                // inbox processing has everything it needs without touching disk staging.
                FileMetadata = fileMetadata,

                FileSystemType = stateItem.TransferInstructionSet.FileSystemType,
                TransferFileType = stateItem.TransferInstructionSet.TransferFileType,

                SharedSecretEncryptedKeyHeader = stateItem.TransferInstructionSet.SharedSecretEncryptedKeyHeader,
            };

            // The payloads were already streamed straight to long-term under the incoming fileId during receive.
            // If enqueueing the inbox item fails, nothing will ever process or clean them (the orphan scanner only
            // sweeps the inbox folder, not long-term), so reclaim them here before failing the transfer.
            try
            {
                await transitInboxBoxStorage.AddAsync(item);
            }
            catch (Exception e)
            {
                // Whatever the cause (cancellation included), the item never enqueued, so the payloads streamed to
                // long-term under the incoming fileId are now orphans that nothing reclaims (the orphan scanner only
                // sweeps the inbox folder, not long-term). Clean them up best-effort before propagating. Cancellation
                // is not a failure, so it is not logged as an error, but its orphans still have to be reclaimed.
                if (e is not OperationCanceledException)
                {
                    logger.LogError(e, "Failed to enqueue inbox item for file {file}; cleaning up directly-written " +
                                       "long-term payloads to avoid orphans", stateItem.File);
                }

                try
                {
                    await fileSystem.Storage.CleanupAbandonedLongTermPayloads(stateItem.File, fileMetadata.Payloads, odinContext);
                }
                catch (Exception cleanupEx)
                {
                    logger.LogError(cleanupEx, "Cleanup of directly-written long-term payloads failed for file {file}",
                        stateItem.File);
                }

                throw;
            }

            await mediator.Publish(new InboxItemReceivedNotification()
            {
                TargetDrive = (await driveManager.GetDriveAsync(item.DriveId)).TargetDriveInfo,
                TransferFileType = stateItem.TransferInstructionSet.TransferFileType,
                FileSystemType = item.FileSystemType,
            });

            return PeerResponseCode.AcceptedIntoInbox;
        }

        private async Task EnqueuePeerPushNotificationDistribution(OdinId recipient, AppNotificationOptions options,
            StorageDrive drive,
            IOdinContext odinContext)
        {
            //ISSUE: this is running as the identity uploading the file, which cannot read the ICR key to decrypt the CAT
            // var clientAuthToken = await ResolveClientAccessTokenAsync(recipient, odinContext, false);
            // if (null == clientAuthToken)
            // {
            //     logger.LogDebug("Attempt to distribute to recipient ({r}) who is not connected", recipient);
            //     return;
            // }

            var item = new OutboxFileItem
            {
                Recipient = recipient,
                Priority = 500, //super high priority to ensure these are sent quickly,
                Type = OutboxItemType.PeerPushNotification,
                AttemptCount = 0,
                File = new InternalDriveFileId()
                {
                    DriveId = drive.Id,
                    FileId = SequentialGuid.CreateGuid()
                },
                DependencyFileId = default,
                State = new OutboxItemState
                {
                    TransferInstructionSet = null,
                    OriginalTransitOptions = null,
                    // EncryptedClientAuthToken = clientAuthToken.ToAuthenticationToken().ToPortableBytes(),
                    Data = OdinSystemSerializer.Serialize(new PushNotificationOutboxRecord()
                        {
                            SenderId = odinContext.GetCallerOdinIdOrFail(),
                            Options = options,
                            Timestamp = UnixTimeUtc.Now()
                                .milliseconds
                        })
                        .ToUtf8ByteArray()
                },
            };

            await peerOutbox.AddItemAsync(item, useUpsert: true);
        }

        private async Task<bool> CanDirectWriteFile(Guid driveId, FileMetadata metadata,
            EncryptedRecipientTransferInstructionSet transferInstructionSet, IOdinContext odinContext)
        {
            await Task.CompletedTask;

            //HACK: if it's not a connected token
            if (odinContext.AuthContext.ToLower() != "TransitCapiAuthScheme".ToLower() &&
                odinContext.AuthContext.ToLower() != "AutomatedIdentityAuthScheme".ToLower())
            {
                return false;
            }

            if (metadata.IsEncrypted == false)
            {
                return true;
            }

            //S1100
            if (metadata.IsEncrypted && odinContext.PermissionsContext.TryGetDriveStorageKey(driveId, out _))
            {
                return true;
            }

            //S2210 - comments cannot fall back to inbox
            if (transferInstructionSet.FileSystemType == FileSystemType.Comment)
            {
                throw new OdinSecurityException("Sender cannot write the comment");
            }

            return false;
        }
    }
}