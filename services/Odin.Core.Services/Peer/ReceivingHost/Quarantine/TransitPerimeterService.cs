using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Services.AppNotifications.Data;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Mediator;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.ReceivingHost.Incoming;
using Odin.Core.Services.Peer.ReceivingHost.Quarantine.Filter;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Core.Storage;
using Odin.Core.Time;

namespace Odin.Core.Services.Peer.ReceivingHost.Quarantine
{
    public class TransitPerimeterService : ITransitPerimeterService
    {
        private readonly NotificationDataService _notificationDataService;
        private readonly OdinContextAccessor _contextAccessor;
        private readonly ITransitPerimeterTransferStateService _transitPerimeterTransferStateService;
        private readonly DriveManager _driveManager;
        private readonly TransitInboxBoxStorage _transitInboxBoxStorage;
        private readonly IDriveFileSystem _fileSystem;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly IMediator _mediator;

        public TransitPerimeterService(
            OdinContextAccessor contextAccessor,
            DriveManager driveManager,
            IDriveFileSystem fileSystem,
            TenantSystemStorage tenantSystemStorage,
            IMediator mediator,
            FileSystemResolver fileSystemResolver, NotificationDataService notificationDataService)
        {
            _contextAccessor = contextAccessor;
            _driveManager = driveManager;
            _fileSystem = fileSystem;
            _transitInboxBoxStorage = new TransitInboxBoxStorage(tenantSystemStorage);
            _mediator = mediator;
            _fileSystemResolver = fileSystemResolver;
            _notificationDataService = notificationDataService;

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

        public async Task<HostTransitResponse> FinalizeTransfer(Guid transferStateItemId, FileMetadata fileMetadata)
        {
            var item = await _transitPerimeterTransferStateService.GetStateItem(transferStateItemId);

            if (item.HasAcquiredQuarantinedPart())
            {
                //TODO: how do i know which filter quarantined it??
                await _transitPerimeterTransferStateService.RemoveStateItem(item.Id);
                return new HostTransitResponse() { Code = TransitResponseCode.QuarantinedPayload };
            }

            if (item.HasAcquiredRejectedPart())
            {
                await _transitPerimeterTransferStateService.RemoveStateItem(item.Id);
                return new HostTransitResponse() { Code = TransitResponseCode.Rejected };
            }

            if (item.IsCompleteAndValid())
            {
                var responseCode = await CompleteTransfer(item, fileMetadata);
                await _transitPerimeterTransferStateService.RemoveStateItem(item.Id);
                return new HostTransitResponse() { Code = responseCode };
            }

            throw new HostToHostTransferException("Unhandled error");
        }

        private async Task<TransitResponseCode> CompleteTransfer(IncomingTransferStateItem stateItem, FileMetadata fileMetadata)
        {
            //S0001, S1000, S2000 - can the sender write the content to the target drive?
            _fileSystem.Storage.AssertCanWriteToDrive(stateItem.TempFile.DriveId);

            if (null != stateItem.TransferInstructionSet.AppNotificationOptions)
            {
                await _notificationDataService.EnqueueNotification(new EnqueueNotificationRequest()
                {
                    AppNotificationOptions = stateItem.TransferInstructionSet.AppNotificationOptions
                });
            }
            
            var directWriteSuccess = await TryDirectWriteFile(stateItem, fileMetadata);

            if (directWriteSuccess)
            {
                return TransitResponseCode.AcceptedDirectWrite;
            }

            //S1220
            return await RouteToInbox(stateItem);
        }

        public async Task<HostTransitResponse> AcceptDeleteLinkedFileRequest(TargetDrive targetDrive, Guid globalTransitId, FileSystemType fileSystemType)
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

                    return new HostTransitResponse()
                    {
                        Code = TransitResponseCode.AcceptedDirectWrite,
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

                return new HostTransitResponse()
                {
                    Code = TransitResponseCode.AcceptedIntoInbox,
                    Message = ""
                };
            }
            catch
            {
                //TODO: add logging here?
                return new HostTransitResponse()
                {
                    Code = TransitResponseCode.Rejected,
                    Message = "Server Error"
                };
            }
        }

        public Task<QueryModifiedResult> QueryModified(FileQueryParams qp, QueryModifiedResultOptions options)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(qp.TargetDrive);
            var results = _fileSystem.Query.GetModified(driveId, qp, options);
            return results;
        }

        public Task<QueryBatchCollectionResponse> QueryBatchCollection(QueryBatchCollectionRequest request)
        {
            var results = _fileSystem.Query.GetBatchCollection(request);
            return results;
        }

        public Task<QueryBatchResult> QueryBatch(FileQueryParams qp, QueryBatchResultOptions options)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(qp.TargetDrive);
            var results = _fileSystem.Query.GetBatch(driveId, qp, options);
            return results;
        }

        public async Task<SharedSecretEncryptedFileHeader> GetFileHeader(TargetDrive targetDrive, Guid fileId)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(targetDrive),
                FileId = fileId
            };

            var result = await _fileSystem.Storage.GetSharedSecretEncryptedHeader(file);

            return result;
        }

        public async Task<(string encryptedKeyHeader64, bool isEncrypted, PayloadStream ps)> GetPayloadStream(TargetDrive targetDrive, Guid fileId,
            string key, FileChunk chunk)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(targetDrive),
                FileId = fileId
            };

            var header = await _fileSystem.Storage.GetSharedSecretEncryptedHeader(file);

            if (header == null)
            {
                return (null, default, null);
            }

            if (!(header.FileMetadata.Payloads?.Any(p => string.Equals(p.Key, key, StringComparison.InvariantCultureIgnoreCase)) ?? false))
            {
                return (null, default, null);
            }

            string encryptedKeyHeader64 = header.SharedSecretEncryptedKeyHeader.ToBase64();
            var ps = await _fileSystem.Storage.GetPayloadStream(file, key, chunk);

            if (null == ps)
            {
                throw new OdinClientException("Header file contains payload key but there is no payload stored with that key", OdinClientErrorCode.InvalidFile);
            }

            return (encryptedKeyHeader64, header.FileMetadata.IsEncrypted, ps);
        }

        public async Task<(string encryptedKeyHeader64, bool payloadIsEncrypted, string decryptedContentType, UnixTimeUtc? lastModified, Stream stream)>
            GetThumbnail(TargetDrive targetDrive, Guid fileId, int height, int width, string payloadKey)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(targetDrive),
                FileId = fileId
            };

            var header = await _fileSystem.Storage.GetSharedSecretEncryptedHeader(file);

            var descriptor = header.FileMetadata.GetPayloadDescriptor(payloadKey);
            if (descriptor == null)
            {
                return (null, default, null, null, null);
            }

            var thumbs = descriptor.Thumbnails?.ToList();
            var thumbnail = DriveFileUtility.FindMatchingThumbnail(thumbs, width, height, directMatchOnly: false);
            if (null == thumbnail)
            {
                return (null, default, null, null, null);
            }

            var (thumb, _) = await _fileSystem.Storage.GetThumbnailPayloadStream(file, width, height, payloadKey);
            string encryptedKeyHeader64 = header.SharedSecretEncryptedKeyHeader.ToBase64();
            return (encryptedKeyHeader64, header.FileMetadata.IsEncrypted, thumbnail.ContentType, descriptor.LastModified, thumb);
        }

        public async Task<IEnumerable<PerimeterDriveData>> GetDrives(Guid driveType)
        {
            //filter drives by only returning those the caller can see
            var allDrives = await _driveManager.GetDrives(driveType, PageOptions.All);
            var perms = _contextAccessor.GetCurrent().PermissionsContext;
            var readableDrives = allDrives.Results.Where(drive => perms.HasDrivePermission(drive.Id, DrivePermission.Read));
            return readableDrives.Select(drive => new PerimeterDriveData()
            {
                TargetDrive = drive.TargetDriveInfo,
            });
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
        private async Task<TransitResponseCode> RouteToInbox(IncomingTransferStateItem stateItem)
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

            return TransitResponseCode.AcceptedIntoInbox;
        }
    }
}