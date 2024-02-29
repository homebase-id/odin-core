using System;
using System.IO;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Services.AppNotifications.Push;
using Odin.Core.Services.AppNotifications.SystemNotifications;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Mediator;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Core.Services.Peer.Outgoing.Drive;
using Odin.Core.Storage;
using Odin.Core.Time;

namespace Odin.Core.Services.Peer.Incoming.Drive.Transfer
{
    public class PeerDriveIncomingTransferService
    {
        private readonly PushNotificationService _pushNotificationService;
        private readonly OdinContextAccessor _contextAccessor;
        private readonly ITransitPerimeterTransferStateService _transitPerimeterTransferStateService;
        private readonly DriveManager _driveManager;
        private readonly TransitInboxBoxStorage _transitInboxBoxStorage;
        private readonly IDriveFileSystem _fileSystem;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly IMediator _mediator;

        public PeerDriveIncomingTransferService(
            OdinContextAccessor contextAccessor,
            DriveManager driveManager,
            IDriveFileSystem fileSystem,
            TenantSystemStorage tenantSystemStorage,
            IMediator mediator,
            FileSystemResolver fileSystemResolver, PushNotificationService pushNotificationService)
        {
            _contextAccessor = contextAccessor;
            _driveManager = driveManager;
            _fileSystem = fileSystem;
            _transitInboxBoxStorage = new TransitInboxBoxStorage(tenantSystemStorage);
            _mediator = mediator;
            _fileSystemResolver = fileSystemResolver;
            _pushNotificationService = pushNotificationService;

            _transitPerimeterTransferStateService = new TransitPerimeterTransferStateService(_fileSystem, contextAccessor);
        }

        public async Task<Guid> InitializeIncomingTransfer(EncryptedRecipientTransferInstructionSet transferInstructionSet)
        {
            return await _transitPerimeterTransferStateService.CreateTransferStateItem(transferInstructionSet);
        }

        public async Task AcceptPart(Guid transferStateItemId, MultipartHostTransferParts part, string fileExtension, Stream data)
        {
            var item = await _transitPerimeterTransferStateService.GetStateItem(transferStateItemId);
            await _transitPerimeterTransferStateService.AcceptPart(item.Id, part, fileExtension, data);
        }

        public async Task<PeerTransferResponse> FinalizeTransfer(Guid transferStateItemId, FileMetadata fileMetadata)
        {
            var item = await _transitPerimeterTransferStateService.GetStateItem(transferStateItemId);

            var responseCode = await FinalizeTransferInternal(item, fileMetadata);

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
                        Sender = _contextAccessor.GetCurrent().GetCallerOdinIdOrFail(),
                    });
                }
                else
                {
                    var notificationOptions = item.TransferInstructionSet.AppNotificationOptions;
                    if (null != notificationOptions)
                    {
                        var senderId = _contextAccessor.GetCurrent().GetCallerOdinIdOrFail();
                        await _pushNotificationService.EnqueueNotification(senderId, notificationOptions);
                    }
                }

                await _transitPerimeterTransferStateService.RemoveStateItem(item.Id);
                return new PeerTransferResponse() { Code = responseCode };
            }

            throw new OdinSystemException("Unhandled Routing");
        }

        public async Task<PeerTransferResponse> AcceptDeleteLinkedFileRequest(TargetDrive targetDrive, Guid globalTransitId, FileSystemType fileSystemType)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(targetDrive);

            //TODO: add checks if the sender can write comments if this is a comment
            _fileSystem.Storage.AssertCanWriteToDrive(driveId);

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
                if (header.FileMetadata.SenderOdinId != _contextAccessor.GetCurrent().Caller.OdinId)
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
                Sender = this._contextAccessor.GetCurrent().GetCallerOdinIdOrFail(),

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

        private async Task<PeerResponseCode> FinalizeTransferInternal(IncomingTransferStateItem stateItem, FileMetadata fileMetadata)
        {
            //S0001, S1000, S2000 - can the sender write the content to the target drive?
            _fileSystem.Storage.AssertCanWriteToDrive(stateItem.TempFile.DriveId);

            var directWriteSuccess = await TryDirectWriteFile(stateItem, fileMetadata);

            if (directWriteSuccess)
            {
                return PeerResponseCode.AcceptedDirectWrite;
            }

            //S1220
            return await RouteToInbox(stateItem);
        }

        private async Task<bool> TryDirectWriteFile(IncomingTransferStateItem stateItem, FileMetadata metadata)
        {
            _fileSystem.Storage.AssertCanWriteToDrive(stateItem.TempFile.DriveId);

            //HACK: if it's not a connected token
            if (_contextAccessor.GetCurrent().AuthContext.ToLower() != "TransitCertificate".ToLower())
            {
                return false;
            }

            //TODO: check if any apps are online and we can snag the storage key

            TransitFileWriter writer = new TransitFileWriter(_fileSystemResolver);
            var sender = _contextAccessor.GetCurrent().GetCallerOdinIdOrFail();
            var decryptedKeyHeader = DecryptKeyHeaderWithSharedSecret(stateItem.TransferInstructionSet.SharedSecretEncryptedKeyHeader);

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
                var hasStorageKey = _contextAccessor.GetCurrent().PermissionsContext.TryGetDriveStorageKey(stateItem.TempFile.DriveId, out var _);

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

        private KeyHeader DecryptKeyHeaderWithSharedSecret(EncryptedKeyHeader sharedSecretEncryptedKeyHeader)
        {
            var sharedSecret = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
            var decryptedKeyHeader = sharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
            return decryptedKeyHeader;
        }

        /// <summary>
        /// Stores the file in the inbox so it can be processed by the owner in a separate process
        /// </summary>
        private async Task<PeerResponseCode> RouteToInbox(IncomingTransferStateItem stateItem)
        {
            var item = new TransferInboxItem()
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = this._contextAccessor.GetCurrent().GetCallerOdinIdOrFail(),

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