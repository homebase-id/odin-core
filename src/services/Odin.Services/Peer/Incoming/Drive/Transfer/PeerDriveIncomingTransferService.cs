using System;
using System.IO;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
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
        private readonly PushNotificationService _pushNotificationService;
        private readonly ITransitPerimeterTransferStateService _transitPerimeterTransferStateService;
        private readonly DriveManager _driveManager;
        private readonly TransitInboxBoxStorage _transitInboxBoxStorage;
        private readonly IDriveFileSystem _fileSystem;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly IMediator _mediator;

        public PeerDriveIncomingTransferService(
            DriveManager driveManager,
            IDriveFileSystem fileSystem,
            TenantSystemStorage tenantSystemStorage,
            IMediator mediator,
            FileSystemResolver fileSystemResolver, PushNotificationService pushNotificationService)
        {
            _driveManager = driveManager;
            _fileSystem = fileSystem;
            _transitInboxBoxStorage = new TransitInboxBoxStorage(tenantSystemStorage);
            _mediator = mediator;
            _fileSystemResolver = fileSystemResolver;
            _pushNotificationService = pushNotificationService;

            _transitPerimeterTransferStateService = new TransitPerimeterTransferStateService(_fileSystem);
        }

        public async Task<Guid> InitializeIncomingTransfer(EncryptedRecipientTransferInstructionSet transferInstructionSet, OdinContext odinContext)
        {
            return await _transitPerimeterTransferStateService.CreateTransferStateItem(transferInstructionSet, odinContext);
        }

        public async Task AcceptPart(Guid transferStateItemId, MultipartHostTransferParts part, string fileExtension, Stream data)
        {
            var item = await _transitPerimeterTransferStateService.GetStateItem(transferStateItemId);
            await _transitPerimeterTransferStateService.AcceptPart(item.Id, part, fileExtension, data);
        }

        public async Task<PeerTransferResponse> FinalizeTransfer(Guid transferStateItemId, FileMetadata fileMetadata, OdinContext odinContext)
        {
            var item = await _transitPerimeterTransferStateService.GetStateItem(transferStateItemId);

            var responseCode = await FinalizeTransferInternal(item, fileMetadata, odinContext);

            if (responseCode == PeerResponseCode.AcceptedDirectWrite || responseCode == PeerResponseCode.AcceptedIntoInbox)
            {
                //Feed hack (again)
                if (item.TransferInstructionSet.TargetDrive == SystemDriveConstants.FeedDrive ||
                    item.TransferInstructionSet.TargetDrive.Type == SystemDriveConstants.ChannelDriveType)
                {
                    //Note: we say new feed item here because comments are never pushed into the feed drive; so any
                    //item going into the feed is new content (i.e. post/image, etc.)
                    await _mediator.Publish(new NewFeedItemReceived()
                    {
                        FileSystemType = item.TransferInstructionSet.FileSystemType,
                        Sender = odinContext.GetCallerOdinIdOrFail(),
                    });
                }
                else
                {
                    var notificationOptions = item.TransferInstructionSet.AppNotificationOptions;
                    if (null != notificationOptions)
                    {
                        var senderId = odinContext.GetCallerOdinIdOrFail();
                        await _pushNotificationService.EnqueueNotification(senderId, notificationOptions, odinContext);
                    }
                }

                await _transitPerimeterTransferStateService.RemoveStateItem(item.Id);
                return new PeerTransferResponse() { Code = responseCode };
            }

            throw new OdinSystemException("Unhandled Routing");
        }

        public async Task<PeerTransferResponse> AcceptDeleteLinkedFileRequest(TargetDrive targetDrive, Guid globalTransitId, FileSystemType fileSystemType, OdinContext odinContext)
        {
            var driveId = odinContext.PermissionsContext.GetDriveId(targetDrive);

            //TODO: add checks if the sender can write comments if this is a comment
            await _fileSystem.Storage.AssertCanWriteToDrive(driveId);

            //if the sender can write, we can perform this now

            if (fileSystemType == FileSystemType.Comment)
            {
                //Note: we need to check if the person deleting the comment is the original commenter or the owner
                var header = await _fileSystem.Query.GetFileByGlobalTransitId(driveId, globalTransitId);
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
                });

                return new PeerTransferResponse()
                {
                    Code = PeerResponseCode.AcceptedDirectWrite,
                    Message = ""
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

            await _transitInboxBoxStorage.Add(item);

            return new PeerTransferResponse()
            {
                Code = PeerResponseCode.AcceptedIntoInbox,
                Message = ""
            };
        }

        //

        private async Task<PeerResponseCode> FinalizeTransferInternal(IncomingTransferStateItem stateItem, FileMetadata fileMetadata, OdinContext odinContext)
        {
            //S0001, S1000, S2000 - can the sender write the content to the target drive?
            await _fileSystem.Storage.AssertCanWriteToDrive(stateItem.TempFile.DriveId);

            var directWriteSuccess = await TryDirectWriteFile(stateItem, fileMetadata, odinContext);

            if (directWriteSuccess)
            {
                return PeerResponseCode.AcceptedDirectWrite;
            }

            //S1220
            return await RouteToInbox(stateItem, odinContext);
        }

        private async Task<bool> TryDirectWriteFile(IncomingTransferStateItem stateItem, FileMetadata metadata, OdinContext odinContext)
        {
            await _fileSystem.Storage.AssertCanWriteToDrive(stateItem.TempFile.DriveId);

            //HACK: if it's not a connected token
            if (odinContext.AuthContext.ToLower() != "TransitCertificate".ToLower())
            {
                return false;
            }

            //TODO: check if any apps are online and we can snag the storage key

            PeerFileWriter writer = new PeerFileWriter(_fileSystemResolver);
            var sender = odinContext.GetCallerOdinIdOrFail();
            var decryptedKeyHeader = DecryptKeyHeaderWithSharedSecret(stateItem.TransferInstructionSet.SharedSecretEncryptedKeyHeader, odinContext);

            if (metadata.IsEncrypted == false)
            {
                //S1110 - Write to disk and send notifications
                await writer.HandleFile(stateItem.TempFile, _fileSystem, decryptedKeyHeader, sender, stateItem.TransferInstructionSet);

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
                    await writer.HandleFile(stateItem.TempFile, _fileSystem, decryptedKeyHeader, sender, stateItem.TransferInstructionSet);
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

        private KeyHeader DecryptKeyHeaderWithSharedSecret(EncryptedKeyHeader sharedSecretEncryptedKeyHeader, OdinContext odinContext)
        {
            var sharedSecret = odinContext.PermissionsContext.SharedSecretKey;
            var decryptedKeyHeader = sharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
            return decryptedKeyHeader;
        }

        /// <summary>
        /// Stores the file in the inbox so it can be processed by the owner in a separate process
        /// </summary>
        private async Task<PeerResponseCode> RouteToInbox(IncomingTransferStateItem stateItem, OdinContext odinContext)
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

            await _transitInboxBoxStorage.Add(item);
            await _mediator.Publish(new TransitFileReceivedNotification()
            {
                TempFile = new ExternalFileIdentifier()
                {
                    TargetDrive = _driveManager.GetDrive(item.DriveId).Result.TargetDriveInfo,
                    FileId = item.FileId
                },

                TransferFileType = stateItem.TransferInstructionSet.TransferFileType,
                FileSystemType = item.FileSystemType
            });

            return PeerResponseCode.AcceptedIntoInbox;
        }
    }
}