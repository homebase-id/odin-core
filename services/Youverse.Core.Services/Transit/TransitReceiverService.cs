using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Drives.FileSystem;
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
        private readonly TransitInboxBoxStorage _transitInboxBoxStorage;
        private readonly IPublicKeyService _publicKeyService;
        private readonly FileSystemResolver _fileSystemResolver;

        private readonly StandardFileSystem _fileSystem;

        public TransitReceiverService(DotYouContextAccessor contextAccessor, TransitInboxBoxStorage transitInboxBoxStorage, IPublicKeyService publicKeyService, StandardFileSystem standardFileSystem,
            StandardFileSystem fileSystem, FileSystemResolver fileSystemResolver)
        {
            _contextAccessor = contextAccessor;
            _transitInboxBoxStorage = transitInboxBoxStorage;
            _publicKeyService = publicKeyService;
            _standardFileSystem = standardFileSystem;
            _fileSystem = fileSystem;
            _fileSystemResolver = fileSystemResolver;
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

                    await _transitInboxBoxStorage.MarkComplete(item.DriveId, item.Marker);
                    drivesNeedingACommit.Add(item.DriveId);
                }
                catch (Exception e)
                {
                    await _transitInboxBoxStorage.MarkFailure(item.DriveId, item.Marker);
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
            var list = await _transitInboxBoxStorage.GetPendingItems(driveId);
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
            //TODO: should we lock on the id of the global transit id or client unique id?
            
            IDriveFileSystem fs = _fileSystemResolver.ResolveFileSystem(serverMetadata.FileSystemType);

            var targetDriveId = tempFile.DriveId;

            //
            // first case: If the file does not exist, then just write the file
            //
            if (metadata.AppData.UniqueId.HasValue == false && metadata.GlobalTransitId.HasValue == false)
            {
                await _fileSystem.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, "payload");
                return;
            }

            //
            // Second Case: 
            // If there are both a uniqueId and globalTransitId;
            //  - The files they match must be same file
            //  - The current sender must be the same as the sender of the existing file
            //
            if (metadata.GlobalTransitId.HasValue && metadata.AppData.UniqueId.HasValue)
            {
                SharedSecretEncryptedFileHeader existingFileBySharedSecretEncryptedUniqueId = await _fileSystem.Query.GetFileByClientUniqueId(targetDriveId, metadata.AppData.UniqueId.Value);
                SharedSecretEncryptedFileHeader existingFileByGlobalTransitId = await GetFileByGlobalTransitId(tempFile.DriveId, metadata.GlobalTransitId.GetValueOrDefault());

                if (existingFileBySharedSecretEncryptedUniqueId == null && existingFileByGlobalTransitId == null)
                {
                    // Write a new file
                    await _fileSystem.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, "payload");
                    return;
                }

                //if one has a value and the other does not
                if ((existingFileBySharedSecretEncryptedUniqueId != null && existingFileByGlobalTransitId == null) ||
                    (existingFileBySharedSecretEncryptedUniqueId == null && existingFileByGlobalTransitId != null))
                {
                    throw new DriveSecurityException("Invalid write; UniqueId and GlobalTransitId are not the same file");
                }

                //Must be the same file
                if (existingFileBySharedSecretEncryptedUniqueId.FileId != existingFileByGlobalTransitId.FileId)
                {
                    throw new DriveSecurityException("Invalid write; UniqueId and GlobalTransitId are not the same file");
                }

                existingFileBySharedSecretEncryptedUniqueId.AssertFileIsActive();
                existingFileBySharedSecretEncryptedUniqueId.AssertOriginalSender((DotYouIdentity)metadata.SenderDotYouId, $"Sender does not match original sender");

                //Update existing file
                var targetFile = new InternalDriveFileId()
                {
                    FileId = existingFileBySharedSecretEncryptedUniqueId.FileId,
                    DriveId = targetDriveId
                };

                //note: we also update the key header because it might have been changed by the sender
                await _fileSystem.Storage.OverwriteFile(tempFile, targetFile, keyHeader, metadata, serverMetadata, "payload");
                return;
            }

            //
            // If there is only a unique id, validate sender and upsert file
            //
            if (metadata.AppData.UniqueId.HasValue)
            {
                SharedSecretEncryptedFileHeader existingFileBySharedSecretEncryptedUniqueId = await fs.Query.GetFileByClientUniqueId(targetDriveId, metadata.AppData.UniqueId.Value);

                if (existingFileBySharedSecretEncryptedUniqueId == null)
                {
                    // Write a new file
                    await _fileSystem.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, "payload");
                    return;
                }

                existingFileBySharedSecretEncryptedUniqueId.AssertFileIsActive();
                existingFileBySharedSecretEncryptedUniqueId.AssertOriginalSender((DotYouIdentity)metadata.SenderDotYouId, $"Sender does not match original sender");

                //Update existing file
                var targetFile = new InternalDriveFileId()
                {
                    FileId = existingFileBySharedSecretEncryptedUniqueId.FileId,
                    DriveId = targetDriveId
                };

                //note: we also update the key header because it might have been changed by the sender
                await _fileSystem.Storage.OverwriteFile(tempFile, targetFile, keyHeader, metadata, serverMetadata, "payload");
                return;
            }

            //
            // If there is only a global transit id, validate sender and upsert file
            //
            if (metadata.GlobalTransitId.HasValue)
            {
                SharedSecretEncryptedFileHeader existingFileByGlobalTransitId = await GetFileByGlobalTransitId(tempFile.DriveId, metadata.GlobalTransitId.GetValueOrDefault());

                if (existingFileByGlobalTransitId == null)
                {
                    // Write a new file
                    await _fileSystem.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, "payload");
                    return;
                }

                existingFileByGlobalTransitId.AssertFileIsActive();
                existingFileByGlobalTransitId.AssertOriginalSender((DotYouIdentity)metadata.SenderDotYouId, $"Sender does not match original sender");

                //Update existing file
                var targetFile = new InternalDriveFileId()
                {
                    FileId = existingFileByGlobalTransitId.FileId,
                    DriveId = targetDriveId
                };

                //note: we also update the key header because it might have been changed by the sender
                await _fileSystem.Storage.OverwriteFile(tempFile, targetFile, keyHeader, metadata, serverMetadata, "payload");
                return;
            }

            throw new YouverseSystemException("Transit Receiver has unhandled file update scenario");
        }

        private async Task<SharedSecretEncryptedFileHeader> GetFileByGlobalTransitId(Guid driveId, Guid globalTransitId)
        {
            var existingFile = await _fileSystem.Query.GetFileByGlobalTransitId(driveId, globalTransitId);
            return existingFile;
        }
    }
}