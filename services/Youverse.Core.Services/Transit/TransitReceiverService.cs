using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Drives.FileSystem.Standard;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Incoming;

namespace Youverse.Core.Services.Transit
{
    public class TransitReceiverService : ITransitReceiverService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly StandardFileSystem _standardFileSystem;
        private readonly ITransitBoxService _transitBoxService;
        private readonly IPublicKeyService _publicKeyService;

        private readonly StandardFileSystem _fileSystem;

        public TransitReceiverService(DotYouContextAccessor contextAccessor, ITransitBoxService transitBoxService, IPublicKeyService publicKeyService, StandardFileSystem standardFileSystem,
            StandardFileSystem fileSystem)
        {
            _contextAccessor = contextAccessor;
            _transitBoxService = transitBoxService;
            _publicKeyService = publicKeyService;
            _standardFileSystem = standardFileSystem;
            _fileSystem = fileSystem;
        }

        public async Task ProcessIncomingTransitInstructions(TargetDrive targetDrive)
        {
            var drive = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(targetDrive);
            var items = await GetAcceptedItems(drive);

            var drivesNeedingACommit = new List<Guid>();
            foreach (var item in items)
            {
                try
                {
                    if (item.InstructionType == TransferInstructionType.SaveFile)
                    {
                        await HandleFile(item);
                    }
                    else if (item.InstructionType == TransferInstructionType.DeleteLinkedFile)
                    {
                        await DeleteFile(item);
                    }
                    else if (item.InstructionType == TransferInstructionType.None)
                    {
                        throw new YouverseClientException("Transfer type not specified", YouverseClientErrorCode.TransferTypeNotSpecified);
                    }
                    else
                    {
                        throw new YouverseClientException("Invalid transfer type", YouverseClientErrorCode.InvalidTransferType);
                    }

                    await _transitBoxService.MarkComplete(item.DriveId, item.Marker);
                    drivesNeedingACommit.Add(item.DriveId);
                }
                catch (Exception e)
                {
                    await _transitBoxService.MarkFailure(item.DriveId, item.Marker);
                }
            }

            await _fileSystem.Query.EnsureIndexerCommits(drivesNeedingACommit.Distinct());
        }

        private async Task HandleFile(TransferBoxItem item)
        {
            var tempFile = new InternalDriveFileId()
            {
                DriveId = item.DriveId,
                FileId = item.FileId
            };

            var transferInstructionSet =
                await _fileSystem.Storage.GetDeserializedStream<RsaEncryptedRecipientTransferInstructionSet>(tempFile,
                    MultipartHostTransferParts.TransferKeyHeader.ToString(), StorageDisposition.Temporary);

            var (isValidPublicKey, decryptedAesKeyHeaderBytes) =
                await _publicKeyService.DecryptKeyHeaderUsingOfflineKey(transferInstructionSet.EncryptedAesKeyHeader,
                    transferInstructionSet.PublicKeyCrc);

            if (!isValidPublicKey)
            {
                //TODO: handle when isValidPublicKey = false
                throw new YouverseSecurityException("Public key was invalid");
            }

            var decryptedKeyHeader = KeyHeader.FromCombinedBytes(decryptedAesKeyHeaderBytes);
            decryptedAesKeyHeaderBytes.WriteZeros();

            //TODO: this deserialization would be better in the drive service under the name GetTempMetadata or something
            var metadataStream = await _fileSystem.Storage.GetTempStream(tempFile, MultipartHostTransferParts.Metadata.ToString().ToLower());
            var json = await new StreamReader(metadataStream).ReadToEndAsync();
            metadataStream.Close();

            var metadata = DotYouSystemSerializer.Deserialize<FileMetadata>(json);

            if (null == metadata)
            {
                throw new YouverseClientException("Metadata could not be serialized",
                    YouverseClientErrorCode.MalformedMetadata);
            }

            var serverMetadata = new ServerMetadata()
            {
                //files coming from other systems are only accessible to the owner so
                //the owner can use the UI to pass the file along
                AccessControlList = new AccessControlList()
                {
                    RequiredSecurityGroup = SecurityGroupType.Owner
                }
            };

            metadata!.SenderDotYouId = item.Sender;

            switch (transferInstructionSet.TransferFileType)
            {
                case TransferFileType.CommandMessage:
                    await StoreCommandMessage(tempFile, decryptedKeyHeader, metadata, serverMetadata);
                    break;

                case TransferFileType.Normal:
                    await StoreNormalFileLongTerm(tempFile, decryptedKeyHeader, metadata, serverMetadata);
                    break;

                default:
                    throw new YouverseClientException("Invalid TransferFileType",
                        YouverseClientErrorCode.InvalidTransferFileType);
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

            await _fileSystem.Storage.SoftDeleteLongTermFile(file);
        }

        /// <summary>
        /// Stores an incoming command message and updates the queue
        /// </summary>
        private async Task StoreCommandMessage(InternalDriveFileId tempFile, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)
        {
            serverMetadata.DoNotIndex = true;
            await _fileSystem.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, MultipartHostTransferParts.Payload.ToString());
            await _standardFileSystem.Commands.EnqueueCommandMessage(tempFile.DriveId, new List<Guid>() { tempFile.FileId });
        }

        /// <summary>
        /// Stores a long-term file or overwrites an existing long-term file if a global transit id was set
        /// </summary>
        private async Task StoreNormalFileLongTerm(InternalDriveFileId tempFile, KeyHeader keyHeader,
            FileMetadata metadata, ServerMetadata serverMetadata)
        {
            /*
             *
             So there's a scenario we need to handle where a file is sent from frodo to sam that has a uniqueId.
                I'm wondering what we should do when sam gets a file that has a uniqueId that already exists on his system.  Just noodling aloud here:
                    If the Frodo's uniqueId matches sam's uniqueId:
                    If sam's file has the same globaltransitId as the incoming file, overwrite sam's file
                    if sam's file has a different globaltransitId, reject the transfer?
                    If sam's file does not have a globaltransitId, reject the transfer?
                    if frodo's file does not have a global transitId, reject the transfer?
             */

            //validate there is not already a file with this id
            if (metadata.AppData.UniqueId.HasValue)
            {
                throw new NotImplementedException("need to handle when i receive a file with a clientuniqueId that I already have.");
            }

            if (metadata.GlobalTransitId.HasValue) //TODO: should we lock on the id of the global transit id?
            {
                //see if a file with this global transit id already exists
                ClientFileHeader existingFile = await GetFileByGlobalTransitId(tempFile.DriveId, metadata.GlobalTransitId.GetValueOrDefault());

                if (null != existingFile)
                {
                    if (existingFile.FileState == FileState.Deleted)
                    {
                        throw new YouverseSecurityException($"Cannot reuse a GlobalTransitId.  File with GlobalTransitId:{metadata.GlobalTransitId.GetValueOrDefault()} is already deleted.");
                    }

                    //sender must match the sender on the file of this GlobalTransitId
                    if (metadata.SenderDotYouId != existingFile.FileMetadata.SenderDotYouId)
                    {
                        throw new YouverseSecurityException($"Sender does not match original sender of GlobalTransitId:{metadata.GlobalTransitId.GetValueOrDefault()}");
                    }

                    var targetFile = new InternalDriveFileId()
                    {
                        FileId = existingFile.FileId,
                        DriveId = tempFile.DriveId
                    };

                    //note: we also update the key header because it might have been changed by the sender
                    await _fileSystem.Storage.OverwriteFile(tempFile, targetFile, keyHeader, metadata, serverMetadata, MultipartHostTransferParts.Payload.ToString());
                    return;
                }
                //else there was no file by the global transit id
            }


            await _fileSystem.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, MultipartHostTransferParts.Payload.ToString());
        }

        private async Task<ClientFileHeader> GetFileByGlobalTransitId(Guid driveId, Guid globalTransitId)
        {
            var existingFile = await _fileSystem.Query.GetFileByGlobalTransitId(driveId, globalTransitId);
            return existingFile;
        }
    }
}