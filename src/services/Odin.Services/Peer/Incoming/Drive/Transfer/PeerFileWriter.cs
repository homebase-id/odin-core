using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.DataSubscription;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.Peer.Incoming.Drive.Transfer
{
    //TODO: this should be split into a file writer for comments and a file writer for standard files.

    /// <summary>
    /// Handles the process of writing a file from temp storage to long-term storage
    /// </summary>
    public class PeerFileWriter(ILogger logger, FileSystemResolver fileSystemResolver, DriveManager driveManager)
    {
        public async Task HandleFile(InternalDriveFileId tempFile,
            IDriveFileSystem fs,
            KeyHeader decryptedKeyHeader,
            OdinId sender,
            EncryptedRecipientTransferInstructionSet encryptedRecipientTransferInstructionSet,
            IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var fileSystemType = encryptedRecipientTransferInstructionSet.FileSystemType;
            var transferFileType = encryptedRecipientTransferInstructionSet.TransferFileType;
            var contentsProvided = encryptedRecipientTransferInstructionSet.ContentsProvided;

            FileMetadata metadata = null;
            var metadataMs = await Benchmark.MillisecondsAsync(async () =>
            {
                var bytes = await fs.Storage.GetAllFileBytesForWriting(tempFile, MultipartHostTransferParts.Metadata.ToString().ToLower(), odinContext, cn);

                if (bytes == null)
                {
                    // this is bad error.
                    logger.LogError("Cannot find the metadata file (File:{file} on DriveId:{driveID}) was not found ", tempFile.FileId, tempFile.DriveId);
                    throw new OdinFileWriteException("Missing temp file while processing inbox");
                }

                string json = bytes.ToStringFromUtf8Bytes();

                metadata = OdinSystemSerializer.Deserialize<FileMetadata>(json);

                if (null == metadata)
                {
                    logger.LogError("Metadata file (File:{file} on DriveId:{driveID}) could not be deserialized ", tempFile.FileId, tempFile.DriveId);
                    throw new OdinFileWriteException("Metadata could not be deserialized");
                }
            });

            logger.LogInformation("Get metadata from temp file and deserialize: {ms} ms", metadataMs);

            // Files coming from other systems are only accessible to the owner so
            // the owner can use the UI to pass the file along
            var targetAcl = new AccessControlList()
            {
                RequiredSecurityGroup = SecurityGroupType.Owner
            };

            var drive = await driveManager.GetDrive(tempFile.DriveId, cn);
            var isCollabChannel = drive.Attributes.TryGetValue(FeedDriveDistributionRouter.IsCollaborativeChannel, out string value) 
                                  && bool.TryParse(value, out bool collabChannelFlagValue) 
                                  && collabChannelFlagValue;
            
            //TODO: this might be a hacky place to put this but let's let it cook.  It might better be put into the comment storage
            if (fileSystemType == FileSystemType.Comment)
            {
                targetAcl = await ResetAclForComment(metadata, odinContext, cn);
            }
            else
            {
                //
                // Collab channel hack; need to cleanup location of the IsCollaborativeChannel flag
                //
                if (isCollabChannel)
                {
                    targetAcl = encryptedRecipientTransferInstructionSet.OriginalAcl ?? new AccessControlList()
                    {
                        RequiredSecurityGroup = SecurityGroupType.Owner
                    };
                }
            }

            var serverMetadata = new ServerMetadata()
            {
                FileSystemType = fileSystemType,
                AllowDistribution = isCollabChannel ? true : false, 
                AccessControlList = targetAcl
            };

            metadata!.SenderOdinId = sender;
            switch (transferFileType)
            {
                case TransferFileType.CommandMessage:
                    await StoreCommandMessage(fs, tempFile, decryptedKeyHeader, metadata, serverMetadata, odinContext, cn);
                    break;

                case TransferFileType.Normal:
                    await StoreNormalFileLongTerm(fs, tempFile, decryptedKeyHeader, metadata, serverMetadata, contentsProvided, odinContext, cn);
                    break;

                case TransferFileType.EncryptedFileForFeed:
                    await StoreEncryptedFeedFile(fs, tempFile, decryptedKeyHeader, metadata, serverMetadata, contentsProvided, odinContext, cn);
                    break;

                default:
                    throw new OdinFileWriteException("Invalid TransferFileType");
            }
        }

        private async Task<AccessControlList> ResetAclForComment(FileMetadata metadata, IOdinContext odinContext, DatabaseConnection cn)
        {
            AccessControlList targetAcl;

            var (referencedFs, fileId) = await fileSystemResolver.ResolveFileSystem(metadata.ReferencedFile, odinContext, cn);

            if (null == referencedFs || !fileId.HasValue)
            {
                //TODO file does not exist or some other issue - need clarity on what is happening here
                throw new OdinRemoteIdentityException("Referenced file missing or caller does not have access");
            }

            //
            // Issue - the caller cannot see the ACL because it's only shown to the
            // owner, so we need to forceIncludeServerMetadata
            //

            var referencedFile = await referencedFs.Query.GetFileByGlobalTransitId(fileId.Value.DriveId,
                metadata.ReferencedFile.GlobalTransitId, odinContext: odinContext, forceIncludeServerMetadata: true, cn: cn);

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

        public async Task DeleteFile(IDriveFileSystem fs, TransferInboxItem item, IOdinContext odinContext, DatabaseConnection cn)
        {
            var clientFileHeader = await GetFileByGlobalTransitId(fs, item.DriveId, item.GlobalTransitId, odinContext, cn);

            if (clientFileHeader == null)
            {
                // this is bad error.
                logger.LogError(
                    "While attempting to delete a file - Cannot find the metadata file (global transit id:{globalTransitId} on DriveId:{driveId}) was not found ",
                    item.GlobalTransitId, item.DriveId);
                throw new OdinFileWriteException("Missing file by global transit i3d while file while processing delete request in inbox");
            }

            var file = new InternalDriveFileId()
            {
                FileId = clientFileHeader.FileId,
                DriveId = item.DriveId,
            };

            await fs.Storage.SoftDeleteLongTermFile(file, odinContext, cn);
        }

        /// <summary>
        /// Stores an incoming command message and updates the queue
        /// </summary>
        private async Task StoreCommandMessage(IDriveFileSystem fs, InternalDriveFileId tempFile, KeyHeader keyHeader, FileMetadata metadata,
            ServerMetadata serverMetadata, IOdinContext odinContext, DatabaseConnection cn)
        {
            serverMetadata.DoNotIndex = true;
            await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, odinContext: odinContext, ignorePayload: false, cn: cn);
            await fs.Commands.EnqueueCommandMessage(tempFile.DriveId, [tempFile.FileId], cn);
        }


        private async Task WriteNewFile(IDriveFileSystem fs, InternalDriveFileId tempFile, KeyHeader keyHeader,
            FileMetadata metadata, ServerMetadata serverMetadata, bool ignorePayloads, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var ms = await Benchmark.MillisecondsAsync(async () =>
            {
                metadata.TransitCreated = UnixTimeUtc.Now().milliseconds;
                await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext, cn);
            });

            logger.LogInformation("Handle file->CommitNewFile: {ms} ms", ms);
        }


        private async Task UpdateExistingFile(IDriveFileSystem fs, InternalDriveFileId targetFile, KeyHeader keyHeader,
                FileMetadata metadata, ServerMetadata serverMetadata, bool ignorePayloads, IOdinContext odinContext,
                DatabaseConnection cn)
        {
            //Use the version tag from the recipient's server because it won't match the sender (this is due to the fact a new
            //one is written any time you save a header)
            metadata.TransitUpdated = UnixTimeUtc.Now().milliseconds;
            //note: we also update the key header because it might have been changed by the sender
            await fs.Storage.OverwriteFile(targetFile, targetFile, keyHeader, metadata, serverMetadata, ignorePayload: true, odinContext: odinContext, cn);
        }

        /// <summary>
        /// Stores a long-term file or overwrites an existing long-term file if a global transit id was set
        /// </summary>
        private async Task StoreNormalFileLongTerm(IDriveFileSystem fs, InternalDriveFileId tempFile, KeyHeader keyHeader,
                FileMetadata metadata, ServerMetadata serverMetadata, SendContents contentsProvided, IOdinContext odinContext,
                DatabaseConnection cn)
        {
            var ignorePayloads = contentsProvided.HasFlag(SendContents.Payload) == false;

            //TODO: should we lock on the id of the global transit id or client unique id?

            var targetDriveId = tempFile.DriveId;

            //
            // first case: If the file does not exist, then just write the file 
            //
            if (metadata.AppData.UniqueId.HasValue == false && metadata.GlobalTransitId.HasValue == false)
            {
                await WriteNewFile(fs, tempFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext, cn);
                return;
            }

            SharedSecretEncryptedFileHeader header = null;

            //
            // Second Case: 
            // If there are both a uniqueId and globalTransitId;
            //  - The files they match must be same file
            //  - The current sender must be the same as the sender of the existing file
            //
            if (metadata.GlobalTransitId.HasValue && metadata.AppData.UniqueId.HasValue)
            {
                SharedSecretEncryptedFileHeader existingFileBySharedSecretEncryptedUniqueId =
                    await fs.Query.GetFileByClientUniqueId(targetDriveId, metadata.AppData.UniqueId.Value, odinContext, cn);
                SharedSecretEncryptedFileHeader existingFileByGlobalTransitId =
                    await GetFileByGlobalTransitId(fs, tempFile.DriveId, metadata.GlobalTransitId.GetValueOrDefault(), odinContext, cn);

                // Neither gtid nor uid points to an existing file, so just write a new file
                if (existingFileBySharedSecretEncryptedUniqueId == null && existingFileByGlobalTransitId == null)
                {
                    await WriteNewFile(fs, tempFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext, cn);
                    return;
                }


                //if one has a value and the other does not
                if ((existingFileBySharedSecretEncryptedUniqueId != null && existingFileByGlobalTransitId == null))
                    header = existingFileBySharedSecretEncryptedUniqueId;
                else if ((existingFileBySharedSecretEncryptedUniqueId == null && existingFileByGlobalTransitId != null))
                    header = existingFileByGlobalTransitId;
                else if (existingFileBySharedSecretEncryptedUniqueId.FileId == existingFileByGlobalTransitId.FileId)
                    header = existingFileBySharedSecretEncryptedUniqueId; // equal
                else
                    throw new OdinClientException($"Invalid write; UniqueId (fileId={existingFileBySharedSecretEncryptedUniqueId.FileId}) and GlobalTransitId (fileId={existingFileByGlobalTransitId.FileId}) point to two different fileIds.");
            } 
            else if (metadata.AppData.UniqueId.HasValue)
            {
                //
                // If there is only a unique id, validate sender and upsert file
                //
                header = await fs.Query.GetFileByClientUniqueId(targetDriveId, metadata.AppData.UniqueId.Value, odinContext, cn);

                if (header == null)
                {
                    await WriteNewFile(fs, tempFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext, cn);
                    return;
                }
            }
            else if (metadata.GlobalTransitId.HasValue)
            {
                //
                // If there is only a global transit id, validate sender and upsert file
                //
                // logger.LogInformation("processing incoming file with global transit id");

                header = await GetFileByGlobalTransitId(fs, tempFile.DriveId, metadata.GlobalTransitId.GetValueOrDefault(), odinContext, cn);

                if (header == null)
                {
                    await WriteNewFile(fs, tempFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext, cn);
                    return;
                }
            }
            else 
                throw new OdinSystemException("Transit Receiver has unhandled file update scenario");

            header.AssertFileIsActive();
            header.AssertOriginalSender((OdinId)metadata.SenderOdinId, $"Sender does not match original sender");

            metadata.VersionTag = header.FileMetadata.VersionTag;

            //Update existing file
            var targetFile = new InternalDriveFileId()
            {
                FileId = header.FileId,
                DriveId = targetDriveId
            };

            //note: we also update the key header because it might have been changed by the sender
            await UpdateExistingFile(fs, targetFile, keyHeader, metadata, serverMetadata, true, odinContext, cn);
            return;
        }

        private async Task StoreEncryptedFeedFile(IDriveFileSystem fs, InternalDriveFileId tempFile, KeyHeader keyHeader,
            FileMetadata metadata, ServerMetadata serverMetadata, SendContents contentsProvided, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var ignorePayloads = contentsProvided.HasFlag(SendContents.Payload) == false;

            // TODO: decrypt keyheader from ECC keys; see if I can use the same code to store once i have the raw key header
            //TODO: should we lock on the id of the global transit id or client unique id?

            var targetDriveId = tempFile.DriveId;

            //
            // first case: If the file does not exist, then just write the file
            //
            if (metadata.AppData.UniqueId.HasValue == false && metadata.GlobalTransitId.HasValue == false)
            {
                await WriteNewFile(fs, tempFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext, cn);
                return;
            }

            SharedSecretEncryptedFileHeader header = null;
            bool onlyHere = false;

            //
            // Second Case: 
            // If there are both a uniqueId and globalTransitId;
            //  - The files they match must be same file
            //  - The current sender must be the same as the sender of the existing file
            //
            if (metadata.GlobalTransitId.HasValue && metadata.AppData.UniqueId.HasValue)
            {
                SharedSecretEncryptedFileHeader existingFileBySharedSecretEncryptedUniqueId =
                    await fs.Query.GetFileByClientUniqueId(targetDriveId, metadata.AppData.UniqueId.Value, odinContext, cn);
                SharedSecretEncryptedFileHeader existingFileByGlobalTransitId =
                    await GetFileByGlobalTransitId(fs, tempFile.DriveId, metadata.GlobalTransitId.GetValueOrDefault(), odinContext, cn);

                // Neither gtid nor uid points to an existing file, so just write a new file
                if (existingFileBySharedSecretEncryptedUniqueId == null && existingFileByGlobalTransitId == null)
                {
                    await WriteNewFile(fs, tempFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext, cn);
                    return;
                }

                if ((existingFileBySharedSecretEncryptedUniqueId != null && existingFileByGlobalTransitId == null))
                    header = existingFileBySharedSecretEncryptedUniqueId;
                else if ((existingFileBySharedSecretEncryptedUniqueId == null && existingFileByGlobalTransitId != null))
                    header = existingFileByGlobalTransitId;
                else if (existingFileBySharedSecretEncryptedUniqueId.FileId == existingFileByGlobalTransitId.FileId)
                    header = existingFileBySharedSecretEncryptedUniqueId; // equal
                else
                    throw new OdinClientException($"Invalid write; UniqueId (fileId={existingFileBySharedSecretEncryptedUniqueId.FileId}) and GlobalTransitId (fileId={existingFileByGlobalTransitId.FileId}) point to two different fileIds.");

                onlyHere = true;
            }
            else if (metadata.AppData.UniqueId.HasValue)
            {
                //
                // If there is only a unique id, validate sender and upsert file
                //
                header = await fs.Query.GetFileByClientUniqueId(targetDriveId, metadata.AppData.UniqueId.Value, odinContext, cn);

                if (header == null)
                {
                    await WriteNewFile(fs, tempFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext, cn);
                    return;
                }
            }
            else if (metadata.GlobalTransitId.HasValue)
            {
                //
                // If there is only a global transit id, validate sender and upsert file
                //
                // logger.LogInformation("processing incoming file with global transit id");

                header = await GetFileByGlobalTransitId(fs, tempFile.DriveId, metadata.GlobalTransitId.GetValueOrDefault(), odinContext, cn);

                if (header== null)
                {
                    await WriteNewFile(fs, tempFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext, cn);
                    return;
                }
            }
            else
                throw new OdinSystemException("Transit Receiver has unhandled file update scenario");

            header.AssertFileIsActive();
            header.AssertOriginalSender((OdinId)metadata.SenderOdinId, $"Sender does not match original sender");

            metadata.VersionTag = header.FileMetadata.VersionTag;

            //Update existing file
            var targetFile = new InternalDriveFileId()
            {
                FileId = header.FileId,
                DriveId = targetDriveId
            };

            //Update the reaction preview first since the overwrite method; uses what's on disk
            // we call both of these here because this 'special' feed item hack method for collabgroups


            // WHY ONLY HERE?
            if (onlyHere)
                await fs.Storage.UpdateReactionPreview(targetFile, metadata.ReactionPreview, odinContext, cn);

            //note: we also update the key header because it might have been changed by the sender
            await UpdateExistingFile(fs, targetFile, keyHeader, metadata, serverMetadata, true, odinContext, cn);
        }

        private async Task<SharedSecretEncryptedFileHeader> GetFileByGlobalTransitId(IDriveFileSystem fs, Guid driveId, Guid globalTransitId,
            IOdinContext odinContext, DatabaseConnection cn)
        {
            var existingFile = await fs.Query.GetFileByGlobalTransitId(driveId, globalTransitId, odinContext, cn);
            return existingFile;
        }
    }
}