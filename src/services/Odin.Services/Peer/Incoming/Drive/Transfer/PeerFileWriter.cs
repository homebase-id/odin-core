using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive;
using Serilog;

namespace Odin.Services.Peer.Incoming.Drive.Transfer
{
    //TODO: this should be split into a file writer for comments and a file writer for standard files.

    /// <summary>
    /// Handles the process of writing a file from temp storage to long-term storage
    /// </summary>
    public class PeerFileWriter(FileSystemResolver fileSystemResolver)
    {
        public async Task HandleFile(InternalDriveFileId tempFile,
            IDriveFileSystem fs,
            KeyHeader decryptedKeyHeader,
            OdinId sender,
            EncryptedRecipientTransferInstructionSet encryptedRecipientTransferInstructionSet,
            OdinContext odinContext)
        {
            var fileSystemType = encryptedRecipientTransferInstructionSet.FileSystemType;
            var transferFileType = encryptedRecipientTransferInstructionSet.TransferFileType;
            var contentsProvided = encryptedRecipientTransferInstructionSet.ContentsProvided;

            FileMetadata metadata = null;
            var metadataMs = await Benchmark.MillisecondsAsync(async () =>
            {
                var bytes = await fs.Storage.GetAllFileBytesForWriting(tempFile, MultipartHostTransferParts.Metadata.ToString().ToLower(), odinContext);

                if (bytes == null)
                {
                    // this is bad error.
                    Log.Error("Cannot find the metadata file (File:{file} on DriveId:{driveID}) was not found ", tempFile.FileId, tempFile.DriveId);
                    throw new OdinFileWriteException("Missing temp file while processing inbox");
                }

                string json = bytes.ToStringFromUtf8Bytes();

                metadata = OdinSystemSerializer.Deserialize<FileMetadata>(json);

                if (null == metadata)
                {
                    Log.Error("Metadata file (File:{file} on DriveId:{driveID}) could not be deserialized ", tempFile.FileId, tempFile.DriveId);
                    throw new OdinFileWriteException("Metadata could not be deserialized");
                }
            });

            Log.Information("Get metadata from temp file and deserialize: {ms} ms", metadataMs);

            // Files coming from other systems are only accessible to the owner so
            // the owner can use the UI to pass the file along
            var targetAcl = new AccessControlList()
            {
                RequiredSecurityGroup = SecurityGroupType.Owner
            };

            //TODO: this might be a hacky place to put this but let's let it cook.  It might better be put into the comment storage
            if (fileSystemType == FileSystemType.Comment)
            {
                targetAcl = await ResetAclForComment(metadata, odinContext);
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
                    await StoreCommandMessage(fs, tempFile, decryptedKeyHeader, metadata, serverMetadata, odinContext);
                    break;

                case TransferFileType.Normal:
                    await StoreNormalFileLongTerm(fs, tempFile, decryptedKeyHeader, metadata, serverMetadata, contentsProvided, odinContext);
                    break;

                default:
                    throw new OdinFileWriteException("Invalid TransferFileType");
            }
        }

        private async Task<AccessControlList> ResetAclForComment(FileMetadata metadata, OdinContext odinContext)
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
                metadata.ReferencedFile.GlobalTransitId, odinContext: odinContext, forceIncludeServerMetadata: true);

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

        public async Task DeleteFile(IDriveFileSystem fs, TransferInboxItem item, OdinContext odinContext)
        {
            var clientFileHeader = await GetFileByGlobalTransitId(fs, item.DriveId, item.GlobalTransitId, odinContext);
            var file = new InternalDriveFileId()
            {
                FileId = clientFileHeader.FileId,
                DriveId = item.DriveId,
            };

            await fs.Storage.SoftDeleteLongTermFile(file, odinContext);
        }

        /// <summary>
        /// Stores an incoming command message and updates the queue
        /// </summary>
        private async Task StoreCommandMessage(IDriveFileSystem fs, InternalDriveFileId tempFile, KeyHeader keyHeader, FileMetadata metadata,
            ServerMetadata serverMetadata, OdinContext odinContext)
        {
            serverMetadata.DoNotIndex = true;
            await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, odinContext: odinContext, ignorePayload: false);
            await fs.Commands.EnqueueCommandMessage(tempFile.DriveId, [tempFile.FileId]);
        }

        /// <summary>
        /// Stores a long-term file or overwrites an existing long-term file if a global transit id was set
        /// </summary>
        private async Task StoreNormalFileLongTerm(IDriveFileSystem fs, InternalDriveFileId tempFile, KeyHeader keyHeader,
            FileMetadata metadata, ServerMetadata serverMetadata, SendContents contentsProvided, OdinContext odinContext)
        {
            var ignorePayloads = contentsProvided.HasFlag(SendContents.Payload) == false;

            //TODO: should we lock on the id of the global transit id or client unique id?

            var targetDriveId = tempFile.DriveId;

            //
            // first case: If the file does not exist, then just write the file
            //
            if (metadata.AppData.UniqueId.HasValue == false && metadata.GlobalTransitId.HasValue == false)
            {
                var ms = await Benchmark.MillisecondsAsync(async () =>
                {
                    //
                    metadata.TransitCreated = UnixTimeUtc.Now().milliseconds;
                    await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext);
                });

                Log.Information("Handle file->CommitNewFile: {ms} ms", ms);
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
                    await fs.Query.GetFileByClientUniqueId(targetDriveId, metadata.AppData.UniqueId.Value, odinContext);
                SharedSecretEncryptedFileHeader existingFileByGlobalTransitId =
                    await GetFileByGlobalTransitId(fs, tempFile.DriveId, metadata.GlobalTransitId.GetValueOrDefault(), odinContext);

                if (existingFileBySharedSecretEncryptedUniqueId == null && existingFileByGlobalTransitId == null)
                {
                    // Write a new file
                    metadata.TransitCreated = UnixTimeUtc.Now().milliseconds;
                    await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext);
                    return;
                }

                //if one has a value and the other does not
                if ((existingFileBySharedSecretEncryptedUniqueId != null && existingFileByGlobalTransitId == null) ||
                    (existingFileBySharedSecretEncryptedUniqueId == null && existingFileByGlobalTransitId != null))
                {
                    throw new OdinClientException("Invalid write; UniqueId and GlobalTransitId are not the same file");
                }

                //Must be the same file
                if (existingFileBySharedSecretEncryptedUniqueId.FileId != existingFileByGlobalTransitId.FileId)
                {
                    throw new OdinClientException("Invalid write; UniqueId and GlobalTransitId are not the same file");
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
                await fs.Storage.OverwriteFile(tempFile, targetFile, keyHeader, metadata, serverMetadata, ignorePayload: true, odinContext: odinContext);
                return;
            }

            //
            // If there is only a unique id, validate sender and upsert file
            //
            if (metadata.AppData.UniqueId.HasValue)
            {
                SharedSecretEncryptedFileHeader existingFileBySharedSecretEncryptedUniqueId =
                    await fs.Query.GetFileByClientUniqueId(targetDriveId, metadata.AppData.UniqueId.Value, odinContext);

                if (existingFileBySharedSecretEncryptedUniqueId == null)
                {
                    // Write a new file
                    metadata.TransitCreated = UnixTimeUtc.Now().milliseconds;
                    await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext);
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
                await fs.Storage.OverwriteFile(tempFile, targetFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext);
                return;
            }

            //
            // If there is only a global transit id, validate sender and upsert file
            //
            if (metadata.GlobalTransitId.HasValue)
            {
                Log.Information("processing incoming file with global transit id");

                SharedSecretEncryptedFileHeader existingFileByGlobalTransitId =
                    await GetFileByGlobalTransitId(fs, tempFile.DriveId, metadata.GlobalTransitId.GetValueOrDefault(), odinContext);

                if (existingFileByGlobalTransitId == null)
                {
                    // Write a new file
                    metadata.TransitCreated = UnixTimeUtc.Now().milliseconds;
                    await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext);
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
                await fs.Storage.OverwriteFile(tempFile, targetFile, keyHeader, metadata, serverMetadata, ignorePayload: false, odinContext: odinContext);
                return;
            }

            throw new OdinSystemException("Transit Receiver has unhandled file update scenario");
        }

        private async Task<SharedSecretEncryptedFileHeader> GetFileByGlobalTransitId(IDriveFileSystem fs, Guid driveId, Guid globalTransitId,
            OdinContext odinContext)
        {
            var existingFile = await fs.Query.GetFileByGlobalTransitId(driveId, globalTransitId, odinContext);
            return existingFile;
        }
    }
}