using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Incoming;

namespace Youverse.Core.Services.Transit
{
    public class TransitAppService : ITransitAppService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveService _driveService;
        private readonly ITransitBoxService _transitBoxService;
        private readonly IPublicKeyService _publicKeyService;
        private readonly IDriveQueryService _driveQueryService;

        public TransitAppService(IDriveService driveService, DotYouContextAccessor contextAccessor, ITransitBoxService transitBoxService, IPublicKeyService publicKeyService,
            IDriveQueryService driveQueryService)
        {
            _driveService = driveService;
            _contextAccessor = contextAccessor;
            _transitBoxService = transitBoxService;
            _publicKeyService = publicKeyService;
            _driveQueryService = driveQueryService;
        }

        public async Task ProcessIncomingTransitInstructions(TargetDrive targetDrive)
        {
            var drive = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(targetDrive);
            var items = await GetAcceptedItems(drive);

            foreach (var item in items)
            {
                try
                {
                    if (item.Type == TransferType.FileTransfer)
                    {
                        await StoreLongTerm(item);
                    }
                    else if (item.Type == TransferType.DeleteLinkedFile)
                    {
                        await DeleteFile(item);
                    }
                    else if (item.Type == TransferType.None)
                    {
                        throw new YouverseException("Transfer type not specified");
                    }
                    else
                    {
                        throw new YouverseException("Invalid transfer type");
                    }

                    await _transitBoxService.MarkComplete(item.DriveId, item.Marker);
                }
                catch (Exception e)
                {
                    await _transitBoxService.MarkFailure(item.DriveId, item.Marker);
                }
            }
        }

        public Task<PagedResult<TransferBoxItem>> GetQuarantinedItems(PageOptions pageOptions)
        {
            throw new NotImplementedException();
        }

        private async Task<List<TransferBoxItem>> GetAcceptedItems(Guid driveId)
        {
            var list = await _transitBoxService.GetPendingItems(driveId);
            return list;
        }

        private async Task DeleteFile(TransferBoxItem item)
        {
            var clientFileHeader = await GetFileByGlobalTransitId(item.DriveId, item.GlobalTransitId);
            var file = new InternalDriveFileId()
            {
                FileId = clientFileHeader.FileId,
                DriveId = item.DriveId,
            };

            await _driveService.SoftDeleteLongTermFile(file);
        }

        private async Task StoreLongTerm(TransferBoxItem item)
        {
            var tempFile = new InternalDriveFileId()
            {
                DriveId = item.DriveId,
                FileId = item.FileId
            };

            var transferInstructionSet =
                await _driveService.GetDeserializedStream<RsaEncryptedRecipientTransferInstructionSet>(tempFile, MultipartHostTransferParts.TransferKeyHeader.ToString(), StorageDisposition.Temporary);

            var (isValidPublicKey, decryptedAesKeyHeaderBytes) =
                await _publicKeyService.DecryptKeyHeaderUsingOfflineKey(transferInstructionSet.EncryptedAesKeyHeader, transferInstructionSet.PublicKeyCrc);

            if (!isValidPublicKey)
            {
                //TODO: handle when isValidPublicKey = false
                throw new YouverseSecurityException("Public key was invalid");
            }

            var decryptedKeyHeader = KeyHeader.FromCombinedBytes(decryptedAesKeyHeaderBytes);
            decryptedAesKeyHeaderBytes.WriteZeros();

            //TODO: this deserialization would be better in the drive service under the name GetTempMetadata or something
            var metadataStream = await _driveService.GetTempStream(tempFile, MultipartHostTransferParts.Metadata.ToString().ToLower());
            var json = await new StreamReader(metadataStream).ReadToEndAsync();
            metadataStream.Close();

            var metadata = DotYouSystemSerializer.Deserialize<FileMetadata>(json);

            if (null == metadata)
            {
                throw new YouverseException("Metadata could not be serialized");
            }

            var serverMetadata = new ServerMetadata()
            {
                //files coming from other systems are only accessible to the owner so
                //the owner can use the UI to pass the file along
                AccessControlList = new AccessControlList()
                {
                    RequiredSecurityGroup = SecurityGroupType.Owner
                },
            };

            //validate there is not already a file with this id
            if (metadata.AppData.Id.HasValue)
            {
                throw new NotImplementedException("need to handle when i receive a file with a clientuniqueId that I already have.");
            }

            if (metadata.GlobalTransitId.HasValue) //TODO: should we lock on the id of the global transit id?
            {
                //see if a file with this global transit id already exists
                ClientFileHeader existingFile = await GetFileByGlobalTransitId(item.DriveId, metadata.GlobalTransitId.GetValueOrDefault());

                if (null != existingFile)
                {
                    if (existingFile.FileState == FileState.Deleted)
                    {
                        throw new YouverseSecurityException($"Cannot reuse a GlobalTransitId.  File with GlobalTransitId:{metadata.GlobalTransitId.GetValueOrDefault()} is already deleted.");
                    }

                    //sender must match the sender on the file of this GlobalTransitId
                    if (item.Sender != existingFile.FileMetadata.SenderDotYouId)
                    {
                        throw new YouverseSecurityException($"Sender does not match original sender of GlobalTransitId:{metadata.GlobalTransitId.GetValueOrDefault()}");
                    }

                    var targetFile = new InternalDriveFileId()
                    {
                        FileId = existingFile.FileId,
                        DriveId = item.DriveId
                    };

                    //note: we also update the key header because it might have been changed by the sender
                    await _driveService.OverwriteLongTermWithTempFile(tempFile, targetFile, decryptedKeyHeader, metadata, serverMetadata, MultipartHostTransferParts.Payload.ToString());
                    return;
                }
            }

            metadata!.SenderDotYouId = item.Sender;
            await _driveService.CommitTempFileToLongTerm(tempFile, decryptedKeyHeader, metadata, serverMetadata, MultipartHostTransferParts.Payload.ToString());
        }

        private async Task<ClientFileHeader> GetFileByGlobalTransitId(Guid driveId, Guid globalTransitId)
        {
            var existingFile = await _driveQueryService.GetFileByGlobalTransitId(driveId, globalTransitId);
            return existingFile;
        }
    }
}