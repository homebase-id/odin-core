using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.ReceivingHost.Incoming;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit.ReceivingHost
{
    /// <summary>
    /// Handles the process of writing a file from temp storage to long-term storage
    /// </summary>
    public class TransitFileWriter
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly FileSystemResolver _fileSystemResolver;

        public TransitFileWriter(DotYouContextAccessor contextAccessor,
            FileSystemResolver fileSystemResolver)
        {
            _contextAccessor = contextAccessor;
            _fileSystemResolver = fileSystemResolver;
        }

        public async Task HandleFile(InternalDriveFileId tempFile, IDriveFileSystem fs, KeyHeader decryptedKeyHeader, OdinId sender,
            FileSystemType fileSystemType,
            TransferFileType transferFileType)
        {
            //TODO: this deserialization would be better in the drive service under the name GetTempMetadata or something
            var metadataStream = await fs.Storage.GetTempStreamForWriting(tempFile, MultipartHostTransferParts.Metadata.ToString().ToLower());
            var json = await new StreamReader(metadataStream).ReadToEndAsync();
            metadataStream.Close();

            var metadata = DotYouSystemSerializer.Deserialize<FileMetadata>(json);

            if (null == metadata)
            {
                throw new YouverseClientException("Metadata could not be serialized", YouverseClientErrorCode.MalformedMetadata);
            }

            // Files coming from other systems are only accessible to the owner so
            // the owner can use the UI to pass the file along
            var targetAcl = new AccessControlList()
            {
                RequiredSecurityGroup = SecurityGroupType.Owner
            };

            //TODO: this might be a hacky place to put this but let's let it cook.  It might better be put into the comment storage
            if (fileSystemType == FileSystemType.Comment)
            {
                var (referencedFs, fileId) = await _fileSystemResolver.ResolveFileSystem(metadata.ReferencedFile);

                if (null == referencedFs)
                {
                    //TODO file does not exist or some other issue - need clarity on what is happening here
                    throw new YouverseRemoteIdentityException("Referenced file missing or caller does not have access");
                }

                //
                // Issue - the caller cannot see the ACL because it's only shown to the
                // owner, so we need to forceIncludeServerMetadata
                //

                var referencedFile = await referencedFs.Query.GetFileByGlobalTransitId(fileId.Value.DriveId,
                    metadata.ReferencedFile.GlobalTransitId, forceIncludeServerMetadata: true);

                if (null == referencedFile)
                {
                    //TODO file does not exist or some other issue - need clarity on what is happening here
                    throw new YouverseRemoteIdentityException("Referenced file missing or caller does not have access");
                }

                //TODO: check that the incoming file matches the encryption of the referenced file
                // if(referencedFile.FileMetadata.PayloadIsEncrypted)


                targetAcl = referencedFile.ServerMetadata.AccessControlList;
            }

            var serverMetadata = new ServerMetadata()
            {
                FileSystemType = fileSystemType,
                AllowDistribution = false,
                AccessControlList = targetAcl
            };

            metadata!.SenderOdinId = sender;

            switch (transferFileType)
            {
                case TransferFileType.CommandMessage:
                    await StoreCommandMessage(fs, tempFile, decryptedKeyHeader, metadata, serverMetadata);
                    break;

                case TransferFileType.Normal:
                    await StoreNormalFileLongTerm(fs, tempFile, decryptedKeyHeader, metadata, serverMetadata);
                    break;

                default:
                    throw new YouverseClientException("Invalid TransferFileType", YouverseClientErrorCode.InvalidTransferFileType);
            }
        }

        public async Task DeleteFile(IDriveFileSystem fs, TransferInboxItem item)
        {
            var clientFileHeader = await GetFileByGlobalTransitId(fs, item.DriveId, item.GlobalTransitId);
            var file = new InternalDriveFileId()
            {
                FileId = clientFileHeader.FileId,
                DriveId = item.DriveId,
            };

            await fs.Storage.SoftDeleteLongTermFile(file);
        }

        /// <summary>
        /// Stores an incoming command message and updates the queue
        /// </summary>
        private async Task StoreCommandMessage(IDriveFileSystem fs, InternalDriveFileId tempFile, KeyHeader keyHeader, FileMetadata metadata,
            ServerMetadata serverMetadata)
        {
            serverMetadata.DoNotIndex = true;
            await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, MultipartHostTransferParts.Payload.ToString());
            await fs.Commands.EnqueueCommandMessage(tempFile.DriveId, new List<Guid>() { tempFile.FileId });
        }

        /// <summary>
        /// Stores a long-term file or overwrites an existing long-term file if a global transit id was set
        /// </summary>
        private async Task StoreNormalFileLongTerm(IDriveFileSystem fs, InternalDriveFileId tempFile, KeyHeader keyHeader,
            FileMetadata metadata, ServerMetadata serverMetadata)
        {
            //TODO: should we lock on the id of the global transit id or client unique id?

            var targetDriveId = tempFile.DriveId;

            //
            // first case: If the file does not exist, then just write the file
            //
            if (metadata.AppData.UniqueId.HasValue == false && metadata.GlobalTransitId.HasValue == false)
            {
                //
                await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, "payload");
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
                SharedSecretEncryptedFileHeader existingFileBySharedSecretEncryptedUniqueId =
                    await fs.Query.GetFileByClientUniqueId(targetDriveId, metadata.AppData.UniqueId.Value);
                SharedSecretEncryptedFileHeader existingFileByGlobalTransitId =
                    await GetFileByGlobalTransitId(fs, tempFile.DriveId, metadata.GlobalTransitId.GetValueOrDefault());

                if (existingFileBySharedSecretEncryptedUniqueId == null && existingFileByGlobalTransitId == null)
                {
                    // Write a new file
                    await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, "payload");
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
                existingFileBySharedSecretEncryptedUniqueId.AssertOriginalSender((OdinId)metadata.SenderOdinId, $"Sender does not match original sender");

                //Update existing file
                var targetFile = new InternalDriveFileId()
                {
                    FileId = existingFileBySharedSecretEncryptedUniqueId.FileId,
                    DriveId = targetDriveId
                };

                //note: we also update the key header because it might have been changed by the sender
                await fs.Storage.OverwriteFile(tempFile, targetFile, keyHeader, metadata, serverMetadata, "payload");
                return;
            }

            //
            // If there is only a unique id, validate sender and upsert file
            //
            if (metadata.AppData.UniqueId.HasValue)
            {
                SharedSecretEncryptedFileHeader existingFileBySharedSecretEncryptedUniqueId =
                    await fs.Query.GetFileByClientUniqueId(targetDriveId, metadata.AppData.UniqueId.Value);

                if (existingFileBySharedSecretEncryptedUniqueId == null)
                {
                    // Write a new file
                    await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, "payload");
                    return;
                }

                existingFileBySharedSecretEncryptedUniqueId.AssertFileIsActive();
                existingFileBySharedSecretEncryptedUniqueId.AssertOriginalSender((OdinId)metadata.SenderOdinId, $"Sender does not match original sender");

                //Update existing file
                var targetFile = new InternalDriveFileId()
                {
                    FileId = existingFileBySharedSecretEncryptedUniqueId.FileId,
                    DriveId = targetDriveId
                };

                //note: we also update the key header because it might have been changed by the sender
                await fs.Storage.OverwriteFile(tempFile, targetFile, keyHeader, metadata, serverMetadata, "payload");
                return;
            }

            //
            // If there is only a global transit id, validate sender and upsert file
            //
            if (metadata.GlobalTransitId.HasValue)
            {
                SharedSecretEncryptedFileHeader existingFileByGlobalTransitId =
                    await GetFileByGlobalTransitId(fs, tempFile.DriveId, metadata.GlobalTransitId.GetValueOrDefault());

                if (existingFileByGlobalTransitId == null)
                {
                    // Write a new file
                    await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, "payload");
                    return;
                }

                existingFileByGlobalTransitId.AssertFileIsActive();
                existingFileByGlobalTransitId.AssertOriginalSender((OdinId)metadata.SenderOdinId, $"Sender does not match original sender");

                //Update existing file
                var targetFile = new InternalDriveFileId()
                {
                    FileId = existingFileByGlobalTransitId.FileId,
                    DriveId = targetDriveId
                };

                //note: we also update the key header because it might have been changed by the sender
                await fs.Storage.OverwriteFile(tempFile, targetFile, keyHeader, metadata, serverMetadata, "payload");
                return;
            }

            throw new YouverseSystemException("Transit Receiver has unhandled file update scenario");
        }

        private async Task<SharedSecretEncryptedFileHeader> GetFileByGlobalTransitId(IDriveFileSystem fs, Guid driveId, Guid globalTransitId)
        {
            var existingFile = await fs.Query.GetFileByGlobalTransitId(driveId, globalTransitId);
            return existingFile;
        }
    }
}