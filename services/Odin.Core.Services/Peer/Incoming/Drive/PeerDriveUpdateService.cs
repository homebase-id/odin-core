using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dawn;
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
using Odin.Core.Services.Peer.Incoming.Drive.Filter;
using Odin.Core.Services.Peer.Incoming.Drive.InboxStorage;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Storage;
using Odin.Core.Time;

namespace Odin.Core.Services.Peer.Incoming.Drive
{
    public class PeerDriveUpdateService
    {
        private readonly PushNotificationService _pushNotificationService;
        private readonly OdinContextAccessor _contextAccessor;
        private readonly ITransitPerimeterTransferStateService _transitPerimeterTransferStateService;
        private readonly DriveManager _driveManager;
        private readonly TransitInboxBoxStorage _transitInboxBoxStorage;
        private readonly IDriveFileSystem _fileSystem;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly IMediator _mediator;

        public PeerDriveUpdateService(
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
            Guard.Argument(transferInstructionSet, nameof(transferInstructionSet)).NotNull();
            Guard.Argument(transferInstructionSet.SharedSecretEncryptedKeyHeader.Iv.Length,
                nameof(transferInstructionSet.SharedSecretEncryptedKeyHeader.Iv)).NotEqual(0);
            Guard.Argument(transferInstructionSet.SharedSecretEncryptedKeyHeader.EncryptedAesKey.Length,
                nameof(transferInstructionSet.SharedSecretEncryptedKeyHeader.EncryptedAesKey)).NotEqual(0);

            return await _transitPerimeterTransferStateService.CreateTransferStateItem(transferInstructionSet);
        }

        public async Task<AddPartResponse> ApplyFirstStageFiltering(Guid transferStateItemId, MultipartHostTransferParts part, string fileExtension,
            Stream data)
        {
            var item = await _transitPerimeterTransferStateService.GetStateItem(transferStateItemId);

            if (item.HasAcquiredRejectedPart())
            {
                throw new HostToHostTransferException("Corresponding part has been rejected");
            }

            if (item.HasAcquiredQuarantinedPart())
            {
                //quarantine the rest
                await _transitPerimeterTransferStateService.Quarantine(item.Id, part, fileExtension, data);
            }

            var filterResponse = await ApplyFilters(part, data);

            switch (filterResponse)
            {
                case FilterAction.Accept:
                    await _transitPerimeterTransferStateService.AcceptPart(item.Id, part, fileExtension, data);
                    break;

                case FilterAction.Quarantine:
                    await _transitPerimeterTransferStateService.Quarantine(item.Id, part, fileExtension, data);
                    break;

                case FilterAction.Reject:
                default:
                    await _transitPerimeterTransferStateService.Reject(item.Id, part);
                    break;
            }

            return new AddPartResponse()
            {
                FilterAction = filterResponse
            };
        }

        public async Task<bool> IsFileValid(Guid transferStateItemId)
        {
            var item = await _transitPerimeterTransferStateService.GetStateItem(transferStateItemId);
            return item.IsCompleteAndValid();
        }

        public async Task<PeerTransferResponse> FinalizeTransfer(Guid transferStateItemId, FileMetadata fileMetadata)
        {
            var item = await _transitPerimeterTransferStateService.GetStateItem(transferStateItemId);

            if (item.HasAcquiredQuarantinedPart())
            {
                //TODO: how do i know which filter quarantined it??
                await _transitPerimeterTransferStateService.RemoveStateItem(item.Id);
                return new PeerTransferResponse() { Code = PeerResponseCode.QuarantinedPayload };
            }

            if (item.HasAcquiredRejectedPart())
            {
                await _transitPerimeterTransferStateService.RemoveStateItem(item.Id);
                return new PeerTransferResponse() { Code = PeerResponseCode.Rejected };
            }

            if (item.IsCompleteAndValid())
            {
                var responseCode = await CompleteTransfer(item, fileMetadata);

                if (responseCode == PeerResponseCode.AcceptedDirectWrite ||
                    responseCode == PeerResponseCode.AcceptedIntoInbox)
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
                }

                await _transitPerimeterTransferStateService.RemoveStateItem(item.Id);
                return new PeerTransferResponse() { Code = responseCode };
            }

            throw new HostToHostTransferException("Unhandled error");
        }

        private async Task<PeerResponseCode> CompleteTransfer(IncomingTransferStateItem stateItem, FileMetadata fileMetadata)
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
                if (null != header)
                {
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
            }

            try
            {
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
            catch
            {
                //TODO: add logging here?
                return new PeerTransferResponse()
                {
                    Code = PeerResponseCode.Rejected,
                    Message = "Server Error"
                };
            }
        }
        
        private async Task<FilterAction> ApplyFilters(MultipartHostTransferParts part, Stream data)
        {
            //TODO: when this has the full set of filters
            // applied, we need to spawn into multiple
            // threads/tasks so we don't cause a long delay
            // of deciding on incoming data

            //TODO: will need to come from a configuration list
            var filters = new List<ITransitStreamFilter>()
            {
                new MustBeConnectedOrDataProviderFilter(_contextAccessor)
            };

            var context = new FilterContext()
            {
                Sender = this._contextAccessor.GetCurrent().GetCallerOdinIdOrFail()
            };

            //TODO: this should be executed in parallel
            foreach (var filter in filters)
            {
                var result = await filter.Apply(context, part, data);

                if (result.Recommendation == FilterAction.Reject)
                {
                    return FilterAction.Reject;
                }
            }

            return FilterAction.Accept;
        }

        //
        private async Task<bool> TryDirectWriteFile(IncomingTransferStateItem stateItem, FileMetadata metadata)
        {
            _fileSystem.Storage.AssertCanWriteToDrive(stateItem.TempFile.DriveId);

            //HACK: if it's not a connected token
            if (_contextAccessor.GetCurrent().AuthContext.ToLower() != "TransitCertificate".ToLower())
            {
                return false;
            }

            //TODO: check if any apps are online and we can snag the storage key

            TransitFileWriter writer = new TransitFileWriter(_contextAccessor, _fileSystemResolver);
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