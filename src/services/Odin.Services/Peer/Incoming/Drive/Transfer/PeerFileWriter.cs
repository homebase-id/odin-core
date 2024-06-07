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
                AllowDistribution = isCollabChannel,
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
                    await StoreEncryptedFeedFile(fs, tempFile, decryptedKeyHeader, metadata, serverMetadata, odinContext, cn);
                    break;

                default:
                    throw new OdinFileWriteException("Invalid TransferFileType");
            }
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

        public async Task MarkFileAsRead(IDriveFileSystem fs, TransferInboxItem item, IOdinContext odinContext, DatabaseConnection cn)
        {
            var header = await fs.Query.GetFileByGlobalTransitId(item.DriveId,
                item.GlobalTransitId, odinContext, cn,
                excludePreviewThumbnail: false,
                includeTransferHistory: true);

            if (null == header)
            {
                throw new OdinFileWriteException($"No file found with specified global transit Id ({item.GlobalTransitId}) on driveId({item.DriveId})");
            }

            var recordExists = header.ServerMetadata.TransferHistory.Recipients.TryGetValue(item.Sender, out var transferHistoryItem);

            if (!recordExists || transferHistoryItem == null)
            {
                throw new OdinFileWriteException($"Cannot accept read-receipt; there is no record of having sent this file to {item.Sender}");
            }

            var update = new UpdateTransferHistoryData()
            {
                IsReadByRecipient = true
            };

            var file = new InternalDriveFileId()
            {
                FileId = header.FileId,
                DriveId = item.DriveId
            };
            
            await fs.Storage.UpdateTransferHistory(
                file,
                item.Sender,
                update,
                odinContext,
                cn);
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

            logger.LogDebug("Handle file->CommitNewFile: {ms} ms", ms);
        }


        private async Task UpdateExistingFile(IDriveFileSystem fs, InternalDriveFileId targetFile, KeyHeader keyHeader,
            FileMetadata metadata, ServerMetadata serverMetadata, bool ignorePayloads, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            //Use the version tag from the recipient's server because it won't match the sender (this is due to the fact a new
            //one is written any time you save a header)
            metadata.TransitUpdated = UnixTimeUtc.Now().milliseconds;
            //note: we also update the key header because it might have been changed by the sender
            await fs.Storage.OverwriteFile(targetFile, targetFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext, cn);
        }

        /// <summary>
        /// Stores a long-term file or overwrites an existing long-term file if a global transit id was set
        /// </summary>
        private async Task StoreNormalFileLongTerm(IDriveFileSystem fs, InternalDriveFileId tempFile, KeyHeader keyHeader,
            FileMetadata metadata, ServerMetadata serverMetadata, SendContents contentsProvided, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var ignorePayloads = contentsProvided.HasFlag(SendContents.Payload) == false;
            var targetDriveId = tempFile.DriveId;

            if (metadata.GlobalTransitId.HasValue == false)
            {
                throw new OdinClientException("Must have a global transit id to write peer.", OdinClientErrorCode.InvalidFile);
            }

            SharedSecretEncryptedFileHeader header;

            //
            // Second Case: 
            // If there are both a uniqueId and globalTransitId;
            //  - The files they match must be same file
            //  - The current sender must be the same as the sender of the existing file
            //
            if (metadata.AppData.UniqueId.HasValue)
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
                    throw new OdinClientException(
                        $"Invalid write; UniqueId (fileId={existingFileBySharedSecretEncryptedUniqueId.FileId}) and GlobalTransitId (fileId={existingFileByGlobalTransitId.FileId}) point to two different fileIds.");
            }
            else
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
            await UpdateExistingFile(fs, targetFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext, cn);
        }

        private async Task StoreEncryptedFeedFile(IDriveFileSystem fs, InternalDriveFileId tempFile, KeyHeader keyHeader,
            FileMetadata metadata, ServerMetadata serverMetadata, IOdinContext odinContext, DatabaseConnection cn)
        {
            // Rules:
            // You must have a global transit id to write to the feed drive
            // We never allow uniqueId for the feed drive
            metadata.AppData.UniqueId = null;

            if (metadata.GlobalTransitId.HasValue == false)
            {
                throw new OdinClientException("Must have a global transit id to write to the feed drive.", OdinClientErrorCode.InvalidFile);
            }

            var header = await GetFileByGlobalTransitId(fs, tempFile.DriveId, metadata.GlobalTransitId.GetValueOrDefault(), odinContext, cn);
            if (header == null)
            {
                await WriteNewFile(fs, tempFile, keyHeader, metadata, serverMetadata, ignorePayloads: true, odinContext, cn);
                return;
            }

            header.AssertFileIsActive();
            header.AssertOriginalSender((OdinId)metadata.SenderOdinId, $"Sender does not match original sender");

            metadata.VersionTag = header.FileMetadata.VersionTag;

            //Update existing file
            var targetFile = new InternalDriveFileId()
            {
                FileId = header.FileId,
                DriveId = tempFile.DriveId
            };

            //Update the reaction preview first since the overwrite method; uses what's on disk
            // we call both of these here because this 'special' feed item hack method for collabgroups
            await fs.Storage.UpdateReactionPreview(targetFile, metadata.ReactionPreview, odinContext, cn);

            //note: we also update the key header because it might have been changed by the sender
            await UpdateExistingFile(fs, targetFile, keyHeader, metadata, serverMetadata, ignorePayloads: true, odinContext, cn);
        }

        private async Task<SharedSecretEncryptedFileHeader> GetFileByGlobalTransitId(IDriveFileSystem fs, Guid driveId, Guid globalTransitId,
            IOdinContext odinContext, DatabaseConnection cn)
        {
            var existingFile = await fs.Query.GetFileByGlobalTransitId(driveId, globalTransitId, odinContext, cn);
            return existingFile;
        }
    }
}