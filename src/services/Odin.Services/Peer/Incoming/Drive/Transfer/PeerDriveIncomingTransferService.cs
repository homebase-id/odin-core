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
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
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
        DriveManager driveManager,
        IDriveFileSystem fileSystem,
        IMediator mediator,
        PushNotificationService pushNotificationService,
        PeerOutbox peerOutbox,
        IOdinHttpClientFactory odinHttpClientFactory,
        CircleNetworkService circleNetworkService,
        FileSystemResolver fileSystemResolver,
        OdinConfiguration odinConfiguration,
        TransitInboxBoxStorage transitInboxBoxStorage
    ) : PeerServiceBase(odinHttpClientFactory, circleNetworkService, fileSystemResolver, odinConfiguration)
    {
        private IncomingTransferStateItem _transferState;

        private readonly Dictionary<string, List<string>> _uploadedKeys = new(StringComparer.InvariantCultureIgnoreCase);

        public async Task InitializeIncomingTransfer(EncryptedRecipientTransferInstructionSet transferInstructionSet,
            FileMetadata metadata,
            IOdinContext odinContext)
        {
            var driveId = odinContext.PermissionsContext.GetDriveId(transferInstructionSet.TargetDrive);

            var canDirectWrite = await CanDirectWriteFile(_transferState, metadata, odinContext);

            // Notice here: we always create a new fileId when receiving a new file.
            var file = new TempFile()
            {
                File = await fileSystem.Storage.CreateInternalFileId(driveId),
                StorageType = canDirectWrite ? TempStorageType.Upload : TempStorageType.Inbox
            };
            
            _transferState = new IncomingTransferStateItem(file, transferInstructionSet);

            // Write the instruction set to disk
            await using var stream = new MemoryStream(OdinSystemSerializer.Serialize(transferInstructionSet).ToUtf8ByteArray());
            await fileSystem.Storage.WriteTempStream(file, MultipartHostTransferParts.TransferKeyHeader.ToString().ToLower(), stream,
                odinContext);

            var metadataStream = new MemoryStream(Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(metadata)));
            await fileSystem.Storage.WriteTempStream(_transferState.TempFile, "metadata", metadataStream, odinContext);
        }

        public async Task AcceptPayload(string key, string fileExtension, Stream data, IOdinContext odinContext)
        {
            _uploadedKeys.TryAdd(key, new List<string>());
            await fileSystem.Storage.WriteTempStream(_transferState.TempFile, fileExtension, data, odinContext);
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

            await fileSystem.Storage.WriteTempStream(_transferState.TempFile, fileExtension, data, odinContext);
        }

        public async Task<PeerTransferResponse> FinalizeTransfer(FileMetadata fileMetadata, IOdinContext odinContext)
        {
            var shouldExpectPayload = _transferState.TransferInstructionSet.ContentsProvided.HasFlag(SendContents.Payload);

            // if there are payloads in the descriptor, and they should have been sent
            if (fileMetadata.Payloads.Any() && shouldExpectPayload)
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
                            var drive = await driveManager.GetDriveAsync(_transferState.TransferInstructionSet.TargetDrive);
                            if (!drive.AllowSubscriptions)
                            {
                                throw new OdinSecurityException(
                                    "Attempt to distribute app notifications to drive which does not allow subscriptions");
                            }

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
            var driveId = odinContext.PermissionsContext.GetDriveId(targetDrive);

            //TODO: add checks if the sender can write comments if this is a comment
            await fileSystem.Storage.AssertCanWriteToDrive(driveId, odinContext);

            var drive = await driveManager.GetDriveAsync(driveId);
            if (fileSystemType == FileSystemType.Comment || drive.IsCollaborationDrive())
            {
                //Note: we need to check if the person deleting the comment is the original commenter or the owner
                var header = await fileSystem.Query.GetFileByGlobalTransitId(driveId, globalTransitId, odinContext);
                if (null == header)
                {
                    //TODO: should this be a 404?
                    throw new OdinClientException("Invalid global transit Id");
                }

                header.AssertOriginalSender(odinContext.Caller.OdinId.GetValueOrDefault());
                
                await fileSystem.Storage.SoftDeleteLongTermFile(new InternalDriveFileId()
                    {
                        FileId = header.FileId,
                        DriveId = driveId
                    },
                    odinContext);

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

            return new PeerTransferResponse()
            {
                Code = PeerResponseCode.AcceptedIntoInbox
            };
        }

        public async Task<PeerTransferResponse> MarkFileAsReadAsync(TargetDrive targetDrive, Guid globalTransitId,
            FileSystemType fileSystemType,
            IOdinContext odinContext)
        {
            var driveId = odinContext.PermissionsContext.GetDriveId(targetDrive);

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
                OdinContext = odinContext,
                TargetDrive = targetDrive,
                FileSystemType = fileSystemType,
                TransferFileType = TransferFileType.ReadReceipt,
            });

            return new PeerTransferResponse()
            {
                Code = PeerResponseCode.AcceptedIntoInbox
            };
        }

        //

        private async Task<PeerResponseCode> FinalizeTransferInternal(IncomingTransferStateItem stateItem, FileMetadata fileMetadata,
            IOdinContext odinContext)
        {
            //S0001, S1000, S2000 - can the sender write the content to the target drive?
            await fileSystem.Storage.AssertCanWriteToDrive(stateItem.TempFile.File.DriveId, odinContext);

            var directWriteSuccess = await TryDirectWriteFile(stateItem, fileMetadata, odinContext);

            if (directWriteSuccess)
            {
                return PeerResponseCode.AcceptedDirectWrite;
            }

            //S1220
            return await RouteToInboxAsync(stateItem, odinContext);
        }

        private async Task<bool> TryDirectWriteFile(IncomingTransferStateItem stateItem, FileMetadata metadata, IOdinContext odinContext)
        {
            if (!await CanDirectWriteFile(stateItem, metadata, odinContext))
            {
                return false;
            }

            //TODO: check if any apps are online and we can snag the storage key

            PeerFileWriter writer = new PeerFileWriter(logger, FileSystemResolver, driveManager);
            var sender = odinContext.GetCallerOdinIdOrFail();
            var decryptedKeyHeader =
                DecryptKeyHeaderWithSharedSecret(stateItem.TransferInstructionSet.SharedSecretEncryptedKeyHeader, odinContext);

            if (metadata.IsEncrypted == false)
            {
                //S1110 - Write to disk and send notifications
                await writer.HandleFile(stateItem.TempFile, fileSystem, decryptedKeyHeader, sender, stateItem.TransferInstructionSet,
                    odinContext);

                return true;
            }

            //S1100
            if (metadata.IsEncrypted)
            {
                // Next determine if we can direct write the file
                var hasStorageKey = odinContext.PermissionsContext.TryGetDriveStorageKey(stateItem.TempFile.File.DriveId, out _);

                //S1200
                if (hasStorageKey)
                {
                    //S1205
                    await writer.HandleFile(stateItem.TempFile, fileSystem, decryptedKeyHeader, sender, stateItem.TransferInstructionSet,
                        odinContext);
                    return true;
                }

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
        private async Task<PeerResponseCode> RouteToInboxAsync(IncomingTransferStateItem stateItem, IOdinContext odinContext)
        {
            var item = new TransferInboxItem()
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = odinContext.GetCallerOdinIdOrFail(),

                InstructionType = TransferInstructionType.SaveFile,
                DriveId = stateItem.TempFile.File.DriveId,
                FileId = stateItem.TempFile.File.FileId,
                TransferInstructionSet = stateItem.TransferInstructionSet,

                FileSystemType = stateItem.TransferInstructionSet.FileSystemType,
                TransferFileType = stateItem.TransferInstructionSet.TransferFileType,

                SharedSecretEncryptedKeyHeader = stateItem.TransferInstructionSet.SharedSecretEncryptedKeyHeader,
            };

            await transitInboxBoxStorage.AddAsync(item);
            await mediator.Publish(new InboxItemReceivedNotification()
            {
                TargetDrive = (await driveManager.GetDriveAsync(item.DriveId)).TargetDriveInfo,
                TransferFileType = stateItem.TransferInstructionSet.TransferFileType,
                FileSystemType = item.FileSystemType,
                OdinContext = odinContext,
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

        private async Task<bool> CanDirectWriteFile(IncomingTransferStateItem stateItem, FileMetadata metadata, IOdinContext odinContext)
        {
            await Task.CompletedTask;
            
            //HACK: if it's not a connected token
            if (odinContext.AuthContext.ToLower() != "TransitCertificate".ToLower())
            {
                return false;
            }

            if (metadata.IsEncrypted == false)
            {
                return true;
            }
            
            //S1100
            if (metadata.IsEncrypted && odinContext.PermissionsContext.TryGetDriveStorageKey(stateItem.TempFile.File.DriveId, out _))
            {
                return true;
            }

            return false;
        }
    }
}