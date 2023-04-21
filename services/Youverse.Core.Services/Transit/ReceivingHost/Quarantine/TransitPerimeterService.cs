using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Query;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Drives.FileSystem.Base;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.ReceivingHost.Incoming;
using Youverse.Core.Services.Transit.ReceivingHost.Quarantine.Filter;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit.ReceivingHost.Quarantine
{
    public class TransitPerimeterService : ITransitPerimeterService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ITransitPerimeterTransferStateService _transitPerimeterTransferStateService;
        private readonly IPublicKeyService _publicKeyService;
        private readonly DriveManager _driveManager;
        private readonly TransitInboxBoxStorage _transitInboxBoxStorage;
        private readonly IDriveFileSystem _fileSystem;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly IMediator _mediator;

        public TransitPerimeterService(
            DotYouContextAccessor contextAccessor,
            IPublicKeyService publicKeyService,
            DriveManager driveManager,
            IDriveFileSystem fileSystem,
            TenantSystemStorage tenantSystemStorage,
            IMediator mediator,
            FileSystemResolver fileSystemResolver)
        {
            _contextAccessor = contextAccessor;
            _publicKeyService = publicKeyService;
            _driveManager = driveManager;
            _fileSystem = fileSystem;
            _transitInboxBoxStorage = new TransitInboxBoxStorage(tenantSystemStorage);
            _mediator = mediator;
            _fileSystemResolver = fileSystemResolver;

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

            if(fileSystemType == FileSystemType.Comment)
            {
                //Note: we need to check if the person deleting the comment is the original commenter or the owner
                var header = await _fileSystem.Query.GetFileByGlobalTransitId(driveId, globalTransitId);
                if (null != header)
                {

                    //requester must be the original commenter
                    if (header.FileMetadata.SenderOdinId != _contextAccessor.GetCurrent().Caller.OdinId)
                    {
                        throw new YouverseSecurityException();
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

        public async Task<(string encryptedKeyHeader64, bool payloadIsEncrypted, string decryptedContentType, Stream stream)> GetPayloadStream(
            TargetDrive targetDrive, Guid fileId, FileChunk chunk)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(targetDrive),
                FileId = fileId
            };

            var header = await _fileSystem.Storage.GetSharedSecretEncryptedHeader(file);

            if (header == null)
            {
                return (null, default, null, null);
            }

            string encryptedKeyHeader64 = header.SharedSecretEncryptedKeyHeader.ToBase64();
            var payload = await _fileSystem.Storage.GetPayloadStream(file, chunk);

            return (encryptedKeyHeader64, header.FileMetadata.PayloadIsEncrypted, header.FileMetadata.ContentType, payload);
        }

        public async Task<(string encryptedKeyHeader64, bool payloadIsEncrypted, string decryptedContentType, Stream stream)> GetThumbnail(
            TargetDrive targetDrive, Guid fileId, int height, int width)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(targetDrive),
                FileId = fileId
            };

            var header = await _fileSystem.Storage.GetSharedSecretEncryptedHeader(file);
            string encryptedKeyHeader64 = header.SharedSecretEncryptedKeyHeader.ToBase64();

            // Exact duplicate of the code in DriveService.GetThumbnailPayloadStream
            var thumbs = header.FileMetadata.AppData.AdditionalThumbnails?.ToList();
            if (null == thumbs || !thumbs.Any())
            {
                return (null, default, null, null);
            }

            var directMatchingThumb = thumbs.SingleOrDefault(t => t.PixelHeight == height && t.PixelWidth == width);
            if (null != directMatchingThumb)
            {
                var innerThumb = await _fileSystem.Storage.GetThumbnailPayloadStream(file, width, height);
                return (encryptedKeyHeader64, header.FileMetadata.PayloadIsEncrypted, directMatchingThumb.ContentType, innerThumb);
            }

            //TODO: add more logic here to compare width and height separately or together
            var nextSizeUp = thumbs.FirstOrDefault(t => t.PixelHeight > height || t.PixelWidth > width);
            if (null == nextSizeUp)
            {
                nextSizeUp = thumbs.LastOrDefault();
                if (null == nextSizeUp)
                {
                    return (null, default, null, null);
                }
            }

            var thumb = await _fileSystem.Storage.GetThumbnailPayloadStream(file, width, height);
            return (encryptedKeyHeader64, header.FileMetadata.PayloadIsEncrypted, nextSizeUp.ContentType, thumb);
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

            if (metadata.PayloadIsEncrypted == false)
            {
                //S1110 - Write to disk and send notifications
                await writer.HandleFile(stateItem.TempFile, _fileSystem, decryptedKeyHeader, sender,
                    stateItem.TransferInstructionSet.FileSystemType, stateItem.TransferInstructionSet.TransferFileType);

                return true;
            }

            //S1100
            if (metadata.PayloadIsEncrypted)
            {
                // Next determine if we can direct write the file
                var hasStorageKey = _contextAccessor.GetCurrent().PermissionsContext.TryGetDriveStorageKey(stateItem.TempFile.DriveId, out var _);

                //S1200
                if (hasStorageKey)
                {
                    //S1205
                    await writer.HandleFile(stateItem.TempFile, _fileSystem, decryptedKeyHeader, sender,
                        stateItem.TransferInstructionSet.FileSystemType,
                        stateItem.TransferInstructionSet.TransferFileType);
                    return true;
                }

                //S2210 - comments cannot fall back to inbox
                if (stateItem.TransferInstructionSet.FileSystemType == FileSystemType.Comment)
                {
                    throw new YouverseSecurityException("Sender cannot write the comment");
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

        private async Task<FileMetadata> LoadMetadataFromTemp(InternalDriveFileId file)
        {
            var metadataStream = await _fileSystem.Storage.GetTempStream(file, MultipartHostTransferParts.Metadata.ToString().ToLower());
            var json = await new StreamReader(metadataStream).ReadToEndAsync();
            metadataStream.Close();

            var metadata = DotYouSystemSerializer.Deserialize<FileMetadata>(json);
            return metadata;
        }

        /// <summary>
        /// Stores the file in the inbox so it can be processed by the owner in a separate process
        /// </summary>
        private async Task<TransitResponseCode> RouteToInbox(IncomingTransferStateItem stateItem)
        {
            //S1210 - Convert to Rsa encrypted header so this could be handled by the TransitInboxProcessor
            var (rsaEncryptedKeyHeader, crc32) = await ConvertKeyHeaderToRsa(stateItem.TransferInstructionSet.SharedSecretEncryptedKeyHeader);

            var item = new TransferInboxItem()
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = this._contextAccessor.GetCurrent().GetCallerOdinIdOrFail(),

                InstructionType = TransferInstructionType.SaveFile,
                DriveId = stateItem.TempFile.DriveId,
                FileId = stateItem.TempFile.FileId,
                FileSystemType = stateItem.TransferInstructionSet.FileSystemType,
                TransferFileType = stateItem.TransferInstructionSet.TransferFileType,

                PublicKeyCrc = crc32,
                RsaEncryptedKeyHeader = rsaEncryptedKeyHeader
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

        private async Task<(byte[] rsaEncryptedKeyHeader, UInt32 crc)> ConvertKeyHeaderToRsa(EncryptedKeyHeader sharedSecretEncryptedKeyHeader)
        {
            var decryptedKeyHeader = DecryptKeyHeaderWithSharedSecret(sharedSecretEncryptedKeyHeader);
            var pk = await _publicKeyService.GetOfflinePublicKey();
            var publicKey = RsaPublicKeyData.FromDerEncodedPublicKey(pk.publicKey);
            var combinedKey = decryptedKeyHeader.Combine();
            var rsaEncryptedKeyHeader = publicKey.Encrypt(combinedKey.GetKey());
            combinedKey.Wipe();
            return (rsaEncryptedKeyHeader, pk.crc32c);
        }
    }
}