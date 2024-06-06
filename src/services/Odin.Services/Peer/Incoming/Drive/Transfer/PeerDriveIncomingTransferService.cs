using System;
using System.IO;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Services.AppNotifications.Push;
using Odin.Services.AppNotifications.SystemNotifications;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.Peer.Incoming.Drive.Transfer
{
    public class PeerDriveIncomingTransferService
    {
        private readonly ILogger<PeerDriveIncomingTransferService> _logger;
        private readonly PushNotificationService _pushNotificationService;
        private readonly ITransitPerimeterTransferStateService _transitPerimeterTransferStateService;
        private readonly DriveManager _driveManager;
        private readonly TransitInboxBoxStorage _transitInboxBoxStorage;
        private readonly IDriveFileSystem _fileSystem;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly IMediator _mediator;

        public PeerDriveIncomingTransferService(
            ILogger<PeerDriveIncomingTransferService> logger,
            DriveManager driveManager,
            IDriveFileSystem fileSystem,
            TenantSystemStorage tenantSystemStorage,
            IMediator mediator,
            FileSystemResolver fileSystemResolver, PushNotificationService pushNotificationService)
        {
            _logger = logger;
            _driveManager = driveManager;
            _fileSystem = fileSystem;
            _transitInboxBoxStorage = new TransitInboxBoxStorage(tenantSystemStorage);
            _mediator = mediator;
            _fileSystemResolver = fileSystemResolver;
            _pushNotificationService = pushNotificationService;

            _transitPerimeterTransferStateService = new TransitPerimeterTransferStateService(_fileSystem);
        }

        public async Task<Guid> InitializeIncomingTransfer(EncryptedRecipientTransferInstructionSet transferInstructionSet, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            return await _transitPerimeterTransferStateService.CreateTransferStateItem(transferInstructionSet, odinContext, cn);
        }

        public async Task AcceptPart(Guid transferStateItemId, MultipartHostTransferParts part, string fileExtension, Stream data, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var item = await _transitPerimeterTransferStateService.GetStateItem(transferStateItemId);
            await _transitPerimeterTransferStateService.AcceptPart(item.Id, part, fileExtension, data, odinContext, cn);
        }

        public async Task<PeerTransferResponse> FinalizeTransfer(Guid transferStateItemId, FileMetadata fileMetadata, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var item = await _transitPerimeterTransferStateService.GetStateItem(transferStateItemId);

            var responseCode = await FinalizeTransferInternal(item, fileMetadata, odinContext, cn);

            if (responseCode == PeerResponseCode.AcceptedDirectWrite || responseCode == PeerResponseCode.AcceptedIntoInbox)
            {
                //Feed hack (again)
                if (item.TransferInstructionSet.TargetDrive == SystemDriveConstants.FeedDrive ||
                    item.TransferInstructionSet.TargetDrive.Type == SystemDriveConstants.ChannelDriveType)
                {
                    //Note: we say new feed item here because comments are never pushed into the feed drive; so any
                    //item going into the feed is new content (i.e. post/image, etc.)
                    await _mediator.Publish(new NewFeedItemReceived
                    {
                        FileSystemType = item.TransferInstructionSet.FileSystemType,
                        Sender = odinContext.GetCallerOdinIdOrFail(),
                        OdinContext = odinContext,
                        DatabaseConnection = cn
                    });
                }
                else
                {
                    var notificationOptions = item.TransferInstructionSet.AppNotificationOptions;
                    if (null != notificationOptions)
                    {
                        var senderId = odinContext.GetCallerOdinIdOrFail();
                        var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);
                        await _pushNotificationService.EnqueueNotification(senderId, notificationOptions, newContext, cn);
                    }
                }

                await _transitPerimeterTransferStateService.RemoveStateItem(item.Id);
                return new PeerTransferResponse() { Code = responseCode };
            }

            throw new OdinSystemException("Unhandled Routing");
        }

        public async Task<PeerTransferResponse> AcceptDeleteLinkedFileRequest(TargetDrive targetDrive, Guid globalTransitId, FileSystemType fileSystemType,
            IOdinContext odinContext, DatabaseConnection cn)
        {
            var driveId = odinContext.PermissionsContext.GetDriveId(targetDrive);

            //TODO: add checks if the sender can write comments if this is a comment
            await _fileSystem.Storage.AssertCanWriteToDrive(driveId, odinContext, cn);

            //if the sender can write, we can perform this now

            if (fileSystemType == FileSystemType.Comment)
            {
                //Note: we need to check if the person deleting the comment is the original commenter or the owner
                var header = await _fileSystem.Query.GetFileByGlobalTransitId(driveId, globalTransitId, odinContext, cn);
                if (null == header)
                {
                    //TODO: should this be a 404?
                    throw new OdinClientException("Invalid global transit Id");
                }

                //requester must be the original commenter
                if (header.FileMetadata.SenderOdinId != odinContext.Caller.OdinId)
                {
                    throw new OdinSecurityException("Requester must be the original commenter");
                }

                await _fileSystem.Storage.SoftDeleteLongTermFile(new InternalDriveFileId()
                    {
                        FileId = header.FileId,
                        DriveId = driveId
                    },
                    odinContext,
                    cn);

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
                GlobalTransitId = globalTransitId,
                FileSystemType = fileSystemType,
            };

            await _transitInboxBoxStorage.Add(item, cn);

            return new PeerTransferResponse()
            {
                Code = PeerResponseCode.AcceptedIntoInbox
            };
        }

        public async Task<PeerTransferResponse> MarkFileAsRead(TargetDrive targetDrive, Guid globalTransitId, FileSystemType fileSystemType,
            IOdinContext odinContext, DatabaseConnection cn)
        {
            var driveId = odinContext.PermissionsContext.GetDriveId(targetDrive);

            await _fileSystem.Storage.AssertCanWriteToDrive(driveId, odinContext, cn);

            var item = new TransferInboxItem()
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = odinContext.GetCallerOdinIdOrFail(),
                InstructionType = TransferInstructionType.ReadReceipt,
                DriveId = driveId,
                GlobalTransitId = globalTransitId,
                FileSystemType = fileSystemType,
            };

            await _transitInboxBoxStorage.Add(item, cn);

            await _mediator.Publish(new InboxItemReceivedNotification
            {
                OdinContext = odinContext,
                TempFile = null,
                DriveId = driveId,
                FileSystemType = fileSystemType,
                TransferFileType = TransferFileType.ReadReceipt,
                DatabaseConnection = cn
            });

            return new PeerTransferResponse()
            {
                Code = PeerResponseCode.AcceptedIntoInbox
            };
        }

        //

        private async Task<PeerResponseCode> FinalizeTransferInternal(IncomingTransferStateItem stateItem, FileMetadata fileMetadata, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            //S0001, S1000, S2000 - can the sender write the content to the target drive?
            await _fileSystem.Storage.AssertCanWriteToDrive(stateItem.TempFile.DriveId, odinContext, cn);

            var directWriteSuccess = await TryDirectWriteFile(stateItem, fileMetadata, odinContext, cn);

            if (directWriteSuccess)
            {
                return PeerResponseCode.AcceptedDirectWrite;
            }

            //S1220
            return await RouteToInbox(stateItem, odinContext, cn);
        }

        private async Task<bool> TryDirectWriteFile(IncomingTransferStateItem stateItem, FileMetadata metadata, IOdinContext odinContext, DatabaseConnection cn)
        {
            await _fileSystem.Storage.AssertCanWriteToDrive(stateItem.TempFile.DriveId, odinContext, cn);

            //HACK: if it's not a connected token
            if (odinContext.AuthContext.ToLower() != "TransitCertificate".ToLower())
            {
                return false;
            }

            //TODO: check if any apps are online and we can snag the storage key

            PeerFileWriter writer = new PeerFileWriter(_logger, _fileSystemResolver, _driveManager);
            var sender = odinContext.GetCallerOdinIdOrFail();
            var decryptedKeyHeader = DecryptKeyHeaderWithSharedSecret(stateItem.TransferInstructionSet.SharedSecretEncryptedKeyHeader, odinContext);

            if (metadata.IsEncrypted == false)
            {
                //S1110 - Write to disk and send notifications
                await writer.HandleFile(stateItem.TempFile, _fileSystem, decryptedKeyHeader, sender, stateItem.TransferInstructionSet, odinContext, cn);

                return true;
            }

            //S1100
            if (metadata.IsEncrypted)
            {
                // Next determine if we can direct write the file
                var hasStorageKey = odinContext.PermissionsContext.TryGetDriveStorageKey(stateItem.TempFile.DriveId, out var _);

                //S1200
                if (hasStorageKey)
                {
                    //S1205
                    await writer.HandleFile(stateItem.TempFile, _fileSystem, decryptedKeyHeader, sender, stateItem.TransferInstructionSet, odinContext, cn);
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
        /// Stores the file in the inbox so it can be processed by the owner in a separate process
        /// </summary>
        private async Task<PeerResponseCode> RouteToInbox(IncomingTransferStateItem stateItem, IOdinContext odinContext, DatabaseConnection cn)
        {
            var item = new TransferInboxItem()
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = odinContext.GetCallerOdinIdOrFail(),

                InstructionType = TransferInstructionType.SaveFile,
                DriveId = stateItem.TempFile.DriveId,
                FileId = stateItem.TempFile.FileId,
                TransferInstructionSet = stateItem.TransferInstructionSet,

                FileSystemType = stateItem.TransferInstructionSet.FileSystemType,
                TransferFileType = stateItem.TransferInstructionSet.TransferFileType,

                SharedSecretEncryptedKeyHeader = stateItem.TransferInstructionSet.SharedSecretEncryptedKeyHeader,
            };

            await _transitInboxBoxStorage.Add(item, cn);
            await _mediator.Publish(new InboxItemReceivedNotification()
            {
                TempFile = new ExternalFileIdentifier()
                {
                    TargetDrive = _driveManager.GetDrive(item.DriveId, cn).Result.TargetDriveInfo,
                    FileId = item.FileId
                },

                TransferFileType = stateItem.TransferInstructionSet.TransferFileType,
                FileSystemType = item.FileSystemType,
                OdinContext = odinContext,
                DatabaseConnection = cn
            });

            return PeerResponseCode.AcceptedIntoInbox;
        }
    }
}