using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Query;
using Youverse.Core.Services.Drives.FileSystem;
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
    public class TransitPerimeterService : TransitServiceBase<ITransitPerimeterService>, ITransitPerimeterService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ITransitPerimeterTransferStateService _transitPerimeterTransferStateService;
        private readonly IPublicKeyService _publicKeyService;
        private readonly DriveManager _driveManager;
        private readonly TransitInboxBoxStorage _transitInboxBoxStorage;
        private readonly IDriveFileSystem _fileSystem;
        private readonly IMediator _mediator;

        public TransitPerimeterService(
            DotYouContextAccessor contextAccessor,
            IPublicKeyService publicKeyService,
            DriveManager driveManager,
            IDriveFileSystem fileSystem,
            ITenantSystemStorage tenantSystemStorage,
            IMediator mediator)
        {
            _contextAccessor = contextAccessor;
            _publicKeyService = publicKeyService;
            _driveManager = driveManager;
            _fileSystem = fileSystem;
            _transitInboxBoxStorage = new TransitInboxBoxStorage(tenantSystemStorage);
            _mediator = mediator;

            _transitPerimeterTransferStateService = new TransitPerimeterTransferStateService(_fileSystem, contextAccessor);
        }

        public async Task<Guid> InitializeIncomingTransfer(RsaEncryptedRecipientTransferInstructionSet transferInstructionSet)
        {
            Guard.Argument(transferInstructionSet, nameof(transferInstructionSet)).NotNull();
            Guard.Argument(transferInstructionSet!.PublicKeyCrc, nameof(transferInstructionSet.PublicKeyCrc)).NotEqual<uint>(0);
            Guard.Argument(transferInstructionSet.EncryptedAesKeyHeader.Length, nameof(transferInstructionSet.EncryptedAesKeyHeader)).NotEqual(0);

            if (!await _publicKeyService.IsValidPublicKey(transferInstructionSet.PublicKeyCrc))
            {
                throw new YouverseClientException("Invalid Public Key CRC provided", YouverseClientErrorCode.InvalidInstructionSet);
            }

            return await _transitPerimeterTransferStateService.CreateTransferStateItem(transferInstructionSet);
        }

        public async Task<AddPartResponse> ApplyFirstStageFiltering(Guid transferStateItemId, MultipartHostTransferParts part, string fileExtension, Stream data)
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

        public async Task<HostTransitResponse> FinalizeTransfer(Guid transferStateItemId)
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
                await CompleteTransfer(item);
                await _transitPerimeterTransferStateService.RemoveStateItem(item.Id);
                return new HostTransitResponse() { Code = TransitResponseCode.Accepted };
            }

            throw new HostToHostTransferException("Unhandled error");
        }

        private async Task CompleteTransfer(IncomingTransferStateItem stateItem)
        {
            var item = new TransferBoxItem()
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = this._contextAccessor.GetCurrent().GetCallerOdinIdOrFail(),
                PublicKeyCrc = stateItem.PublicKeyCrc,

                InstructionType = TransferInstructionType.SaveFile,
                DriveId = stateItem.TempFile.DriveId,
                FileId = stateItem.TempFile.FileId,
                FileSystemType = stateItem.TransferFileSystemType
            };

            await _transitInboxBoxStorage.Add(item);
            await _mediator.Publish(new TransitFileReceivedNotification()
            {
                TempFile = new ExternalFileIdentifier()
                {
                    TargetDrive = _driveManager.GetDrive(item.DriveId).Result.TargetDriveInfo,
                    FileId = item.FileId
                },
                
                FileSystemType = item.FileSystemType
            });
        }

        public async Task<HostTransitResponse> AcceptDeleteLinkedFileRequest(TargetDrive targetDrive, Guid globalTransitId, FileSystemType fileSystemType)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(targetDrive);

            try
            {
                uint publicKeyCrc = 0; //TODO: we need to encrypt the delete linked file request using the public key or the shared secret
                var item = new TransferBoxItem()
                {
                    Id = Guid.NewGuid(),
                    AddedTimestamp = UnixTimeUtc.Now(),
                    Sender = this._contextAccessor.GetCurrent().GetCallerOdinIdOrFail(),
                    PublicKeyCrc = publicKeyCrc,

                    InstructionType = TransferInstructionType.DeleteLinkedFile,
                    DriveId = driveId,
                    GlobalTransitId = globalTransitId,
                    FileSystemType = fileSystemType
                };

                await _transitInboxBoxStorage.Add(item);

                return new HostTransitResponse()
                {
                    Code = TransitResponseCode.Accepted,
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

        public async Task<(string encryptedKeyHeader64, bool payloadIsEncrypted, string decryptedContentType, Stream stream)> GetPayloadStream(TargetDrive targetDrive, Guid fileId)
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
            var payload = await _fileSystem.Storage.GetPayloadStream(file);

            return (encryptedKeyHeader64, header.FileMetadata.PayloadIsEncrypted, header.FileMetadata.ContentType, payload);
        }

        public async Task<(string encryptedKeyHeader64, bool payloadIsEncrypted, string decryptedContentType, Stream stream)> GetThumbnail(TargetDrive targetDrive, Guid fileId, int height, int width)
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
    }
}