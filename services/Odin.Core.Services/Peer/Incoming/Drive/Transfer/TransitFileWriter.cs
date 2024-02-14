using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Services.Peer.Outgoing.Drive;
using Odin.Core.Storage;
using Odin.Core.Time;

namespace Odin.Core.Services.Peer.Incoming.Drive.Transfer
{
    //TODO: this should be split into a file writer for comments and a file writer for standard files.

    /// <summary>
    /// Handles the process of writing a file from temp storage to long-term storage
    /// </summary>
    public class TransitFileWriter(FileSystemResolver fileSystemResolver)
    {
        public async Task HandleFile(InternalDriveFileId tempFile,
            IDriveFileSystem fs,
            KeyHeader decryptedKeyHeader,
            OdinId sender,
            EncryptedRecipientTransferInstructionSet encryptedRecipientTransferInstructionSet)
        {
            var fileSystemType = encryptedRecipientTransferInstructionSet.FileSystemType;
            var transferFileType = encryptedRecipientTransferInstructionSet.TransferFileType;
            var contentsProvided = encryptedRecipientTransferInstructionSet.ContentsProvided;

            var bytes = await fs.Storage.GetAllFileBytesForWriting(tempFile, MultipartHostTransferParts.Metadata.ToString().ToLower());
            string json = bytes.ToStringFromUtf8Bytes();

            var metadata = OdinSystemSerializer.Deserialize<FileMetadata>(json);

            if (null == metadata)
            {
                throw new OdinClientException("Metadata could not be serialized", OdinClientErrorCode.MalformedMetadata);
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
                targetAcl = await ResetAclForComment(fileSystemType, metadata);
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
                    await StoreNormalFileLongTerm(fs, tempFile, decryptedKeyHeader, metadata, serverMetadata, contentsProvided);
                    break;

                default:
                    throw new OdinClientException("Invalid TransferFileType", OdinClientErrorCode.InvalidTransferFileType);
            }
        }

        private async Task<AccessControlList> ResetAclForComment(FileSystemType fileSystemType, FileMetadata metadata)
        {
            AccessControlList targetAcl;

            var (referencedFs, fileId) = await fileSystemResolver.ResolveFileSystem(metadata.ReferencedFile);

            if (null == referencedFs)
            {
                //TODO file does not exist or some other issue - need clarity on what is happening here
                throw new OdinRemoteIdentityException("Referenced file missing or caller does not have access");
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
                throw new OdinRemoteIdentityException("Referenced file missing or caller does not have access");
            }


            //S2040
            if (referencedFile.FileMetadata.IsEncrypted != metadata.IsEncrypted)
            {
                throw new OdinRemoteIdentityException("Referenced filed and metadata payload encryption do not match");
            }

            targetAcl = referencedFile.ServerMetadata.AccessControlList;

            return targetAcl;
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
            await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, ignorePayload: false);
            await fs.Commands.EnqueueCommandMessage(tempFile.DriveId, new List<Guid>() { tempFile.FileId });
        }

        /// <summary>
        /// Stores a long-term file or overwrites an existing long-term file if a global transit id was set
        /// </summary>
        private async Task StoreNormalFileLongTerm(IDriveFileSystem fs, InternalDriveFileId tempFile, KeyHeader keyHeader,
            FileMetadata metadata, ServerMetadata serverMetadata, SendContents contentsProvided)
        {
            var ignorePayloads = contentsProvided.HasFlag(SendContents.Payload) == false;

            //TODO: should we lock on the id of the global transit id or client unique id?

            var targetDriveId = tempFile.DriveId;

            //
            // first case: If the file does not exist, then just write the file
            //
            if (metadata.AppData.UniqueId.HasValue == false && metadata.GlobalTransitId.HasValue == false)
            {
                //
                metadata.TransitCreated = UnixTimeUtc.Now().milliseconds;
                await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, ignorePayloads);
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
                    metadata.TransitCreated = UnixTimeUtc.Now().milliseconds;
                    await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, ignorePayloads);
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

                //Use the version tag from the recipient's server because it won't match the sender (this is due to the fact a new
                //one is written any time you save a header)
                metadata.VersionTag = existingFileBySharedSecretEncryptedUniqueId.FileMetadata.VersionTag;

                metadata.TransitUpdated = UnixTimeUtc.Now().milliseconds;
                //note: we also update the key header because it might have been changed by the sender
                await fs.Storage.OverwriteFile(tempFile, targetFile, keyHeader, metadata, serverMetadata, ignorePayload: true);
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
                    metadata.TransitCreated = UnixTimeUtc.Now().milliseconds;
                    await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, ignorePayloads);
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
                metadata.TransitUpdated = UnixTimeUtc.Now().milliseconds;
                await fs.Storage.OverwriteFile(tempFile, targetFile, keyHeader, metadata, serverMetadata, ignorePayloads);
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
                    metadata.TransitCreated = UnixTimeUtc.Now().milliseconds;
                    await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, ignorePayloads);
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

                metadata.VersionTag = existingFileByGlobalTransitId.FileMetadata.VersionTag;
                //note: we also update the key header because it might have been changed by the sender
                metadata.TransitUpdated = UnixTimeUtc.Now().milliseconds;
                await fs.Storage.OverwriteFile(tempFile, targetFile, keyHeader, metadata, serverMetadata, ignorePayload: false);
                return;
            }

            throw new OdinSystemException("Transit Receiver has unhandled file update scenario");
        }

        private async Task<SharedSecretEncryptedFileHeader> GetFileByGlobalTransitId(IDriveFileSystem fs, Guid driveId, Guid globalTransitId)
        {
            var existingFile = await fs.Query.GetFileByGlobalTransitId(driveId, globalTransitId);
            return existingFile;
        }
    }
}