using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
using Odin.Services.Drives.FileSystem.Base;
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
            bool driveOriginWasCollaborative = false)
        {
            var fileSystemType = encryptedRecipientTransferInstructionSet.FileSystemType;
            var transferFileType = encryptedRecipientTransferInstructionSet.TransferFileType;

            FileMetadata metadata = null;
            var metadataMs = await PerformanceCounter.MeasureExecutionTime("PeerFileWriter HandleFile ReadTempFile", async () =>
            {
                var bytes = await fs.Storage.GetAllFileBytesFromTempFileForWriting(tempFile,
                    MultipartHostTransferParts.Metadata.ToString().ToLower(), odinContext);

                if (bytes == null)
                {
                    // this is bad error.
                    logger.LogError("Cannot find the metadata file (File:{file} on DriveId:{driveID}) was not found ", tempFile.FileId,
                        tempFile.DriveId);
                    throw new OdinFileWriteException("Missing temp file while processing inbox");
                }

                string json = bytes.ToStringFromUtf8Bytes();

                metadata = OdinSystemSerializer.Deserialize<FileMetadata>(json);

                // var theDrive = await driveManager.GetDrive(tempFile.DriveId);
                if (null == metadata)
                {
                    logger.LogError("Metadata file (File:{file} on DriveId:{driveID}) could not be deserialized ", tempFile.FileId,
                        tempFile.DriveId);
                    throw new OdinFileWriteException("Metadata could not be deserialized");
                }
            });

            logger.LogDebug("Get metadata from temp file and deserialize: {ms} ms", metadataMs);

            // Files coming from other systems are only accessible to the owner so
            // the owner can use the UI to pass the file along
            var targetAcl = new AccessControlList()
            {
                RequiredSecurityGroup = SecurityGroupType.Owner
            };

            var drive = await driveManager.GetDriveAsync(tempFile.DriveId);
            var isCollaborationChannel = drive.IsCollaborationDrive();

            //TODO: this might be a hacky place to put this but let's let it cook.  It might better be put into the comment storage
            if (fileSystemType == FileSystemType.Comment)
            {
                targetAcl = await ResetAclForComment(metadata, odinContext);
            }
            else
            {
                //
                // Collab channel hack
                //
                if (isCollaborationChannel)
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
                AllowDistribution = isCollaborationChannel,
                AccessControlList = targetAcl,
            };

            metadata!.SenderOdinId = sender; //in a collab channel this is not the right sender;
            switch (transferFileType)
            {
                case TransferFileType.Normal:
                    await StoreNormalFileLongTermAsync(fs, tempFile, decryptedKeyHeader, metadata, serverMetadata,
                        encryptedRecipientTransferInstructionSet, odinContext);
                    break;

                case TransferFileType.EncryptedFileForFeed:
                case TransferFileType.EncryptedFileForFeedViaTransit:
                    await StoreEncryptedFeedFile(fs, tempFile, decryptedKeyHeader, metadata, serverMetadata, driveOriginWasCollaborative,
                        odinContext);
                    break;

                default:
                    throw new OdinFileWriteException($"Invalid TransferFileType: {transferFileType}");
            }
        }

        public async Task DeleteFile(IDriveFileSystem fs, TransferInboxItem item, IOdinContext odinContext)
        {
            var clientFileHeader = await GetFileByGlobalTransitId(fs, item.DriveId, item.GlobalTransitId, odinContext);

            if (clientFileHeader == null)
            {
                // this is bad error.
                logger.LogError(
                    "While attempting to delete a file - Cannot find the metadata file (global transit id:{globalTransitId} on DriveId:{driveId}) was not found ",
                    item.GlobalTransitId, item.DriveId);
                throw new OdinFileWriteException("Missing file by global transit id while file while processing delete request in inbox");
            }

            var file = new InternalDriveFileId()
            {
                FileId = clientFileHeader.FileId,
                DriveId = item.DriveId,
            };

            await fs.Storage.SoftDeleteLongTermFile(file, odinContext);
        }

        public async Task MarkFileAsRead(IDriveFileSystem fs, TransferInboxItem item, IOdinContext odinContext)
        {
            var header = await fs.Query.GetFileByGlobalTransitId(item.DriveId,
                item.GlobalTransitId,
                odinContext,
                excludePreviewThumbnail: false,
                includeTransferHistory: true);

            if (null == header)
            {
                throw new OdinFileWriteException($"No file found with specified global transit Id ({item.GlobalTransitId}) " +
                                                 $"on driveId({item.DriveId}) (this should have been detected before adding this item to the inbox)");
            }

            if (header.FileState == FileState.Deleted)
            {
                logger.LogDebug("MarkFileAsRead -> Attempted to mark a deleted file as read");
            }

            // disabling validation during june 14 transition period (old files w/o the transfer history, etc.)

            if (header.ServerMetadata.TransferHistory == null || header.ServerMetadata.TransferHistory.Recipients == null)
            {
                logger.LogDebug("MarkFileAsRead -> TransferHistory is null.  File created: {created} and " +
                                "last updated: {updated}", header.FileMetadata.Created, header.FileMetadata.Updated);
            }
            else
            {
                var recordExists = header.ServerMetadata.TransferHistory.Recipients.TryGetValue(item.Sender, out var transferHistoryItem);

                if (!recordExists || transferHistoryItem == null)
                {
                    // throw new OdinFileWriteException($"Cannot accept read-receipt; there is no record of having sent this file to {item.Sender}");
                    logger.LogDebug("Cannot accept read-receipt; there is no record of having sent this file to {sender}", item.Sender);
                }
            }

            // logger.LogDebug("MarkFileAsRead -> Target File: Created:{created}\t TransitCreated:{tc}\t Updated:{updated}\t TransitUpdated: {tcu}",
            //     header.FileMetadata.Created,
            //     header.FileMetadata.TransitCreated,
            //     header.FileMetadata.Updated,
            //     header.FileMetadata.TransitUpdated);

            var update = new UpdateTransferHistoryData()
            {
                IsReadByRecipient = true,
                IsInOutbox = false
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
                odinContext);
        }

        private async Task<AccessControlList> ResetAclForComment(FileMetadata metadata, IOdinContext odinContext)
        {
            AccessControlList targetAcl;

            var (referencedFs, fileId) = await fileSystemResolver.ResolveFileSystem(metadata.ReferencedFile, odinContext);

            if (null == referencedFs || !fileId.HasValue)
            {
                throw new OdinClientException("Referenced file missing or caller does not have access");
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

        private async Task WriteNewFile(IDriveFileSystem fs, InternalDriveFileId tempFile, KeyHeader keyHeader,
            FileMetadata metadata, ServerMetadata serverMetadata, bool ignorePayloads, IOdinContext odinContext)
        {
            var ms = await PerformanceCounter.MeasureExecutionTime("PeerFileWriter WriteNewFile", async () =>
            {
                metadata.TransitCreated = UnixTimeUtc.Now().milliseconds;
                await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext);
            });

            logger.LogDebug("Handle file->CommitNewFile: {ms} ms", ms);
        }


        private async Task UpdateExistingFile(IDriveFileSystem fs, InternalDriveFileId targetFile, KeyHeader keyHeader,
            FileMetadata metadata, ServerMetadata serverMetadata, bool ignorePayloads, IOdinContext odinContext)
        {
            await PerformanceCounter.MeasureExecutionTime("PeerFileWriter UpdateExistingFile", async () =>
            {
                //Use the version tag from the recipient's server because it won't match the sender (this is due to the fact a new
                //one is written any time you save a header)
                metadata.TransitUpdated = UnixTimeUtc.Now().milliseconds;
                //note: we also update the key header because it might have been changed by the sender
                await fs.Storage.OverwriteFile(targetFile, targetFile, keyHeader, metadata, serverMetadata, ignorePayloads, odinContext);
            });
        }

        /// <summary>
        /// Stores a long-term file or overwrites an existing long-term file if a global transit id was set
        /// </summary>
        private async Task StoreNormalFileLongTermAsync(IDriveFileSystem fs, InternalDriveFileId tempFile, KeyHeader keyHeader,
            FileMetadata newMetadata, ServerMetadata serverMetadata,
            EncryptedRecipientTransferInstructionSet encryptedRecipientTransferInstructionSet, IOdinContext odinContext)
        {
            var contentsProvided = encryptedRecipientTransferInstructionSet.ContentsProvided;
            var ignorePayloads = contentsProvided.HasFlag(SendContents.Payload) == false;
            var targetDriveId = tempFile.DriveId;

            if (newMetadata.GlobalTransitId.HasValue == false)
            {
                throw new OdinClientException("Must have a global transit id to write peer.", OdinClientErrorCode.InvalidFile);
            }

            // First we check if we can match the gtid to an existing file on disk.
            // If we can, then the gtid is the winner and decides the matching file
            //

            SharedSecretEncryptedFileHeader header = await GetFileByGlobalTransitId(fs, tempFile.DriveId,
                newMetadata.GlobalTransitId.GetValueOrDefault(), odinContext);

            // If there is no file matching the gtid, let's check if the UID might point to one
            if (header == null && newMetadata.AppData.UniqueId.HasValue)
            {
                header = await fs.Query.GetFileByClientUniqueId(targetDriveId, newMetadata.AppData.UniqueId.Value, odinContext);
            }

            if (header == null)
            {
                // Neither gtid not uid points to an exiting file, so it's a new file
                await WriteNewFile(fs, tempFile, keyHeader, newMetadata, serverMetadata, ignorePayloads, odinContext);
                return;
            }

            header.AssertFileIsActive();
            var drive = await driveManager.GetDriveAsync(targetDriveId);
            if (!drive.IsCollaborationDrive())
            {
                header.AssertOriginalSender((OdinId)newMetadata.SenderOdinId);
            }

            newMetadata.VersionTag = header.FileMetadata.VersionTag;

            //Update existing file
            var targetFile = new InternalDriveFileId()
            {
                FileId = header.FileId,
                DriveId = targetDriveId
            };

            //note: we also update the key header because it might have been changed by the sender
            await UpdateExistingFile(fs, targetFile, keyHeader, newMetadata, serverMetadata, ignorePayloads, odinContext);
        }


        private async Task StoreEncryptedFeedFile(IDriveFileSystem fs, InternalDriveFileId tempFile, KeyHeader keyHeader,
            FileMetadata newMetadata, ServerMetadata serverMetadata, bool driveOriginWasCollaborative, IOdinContext odinContext)
        {
            // Rules:
            // You must have a global transit id to write to the feed drive
            // We never allow uniqueId for the feed drive
            newMetadata.AppData.UniqueId = null;

            if (newMetadata.GlobalTransitId.HasValue == false)
            {
                throw new OdinClientException("Must have a global transit id to write to the feed drive.", OdinClientErrorCode.InvalidFile);
            }

            var header = await GetFileByGlobalTransitId(fs, tempFile.DriveId, newMetadata.GlobalTransitId.GetValueOrDefault(), odinContext);

            if (header == null)
            {
                await WriteNewFile(fs, tempFile, keyHeader, newMetadata, serverMetadata, ignorePayloads: true, odinContext);
                return;
            }

            header.AssertFileIsActive();
            if (!driveOriginWasCollaborative) //collab channel hack to allow multiple editors to the same file
            {
                header.AssertOriginalSender((OdinId)newMetadata.SenderOdinId);
            }

            newMetadata.VersionTag = header.FileMetadata.VersionTag;

            //Update existing file
            var targetFile = new InternalDriveFileId()
            {
                FileId = header.FileId,
                DriveId = tempFile.DriveId
            };

            //Update the reaction preview first since the overwrite method; uses what's on disk
            // we call both of these here because this 'special' feed item hack method for collabgroups
            await fs.Storage.UpdateReactionSummary(targetFile, newMetadata.ReactionPreview, odinContext);
            //note: we also update the key header because it might have been changed by the sender
            await UpdateExistingFile(fs, targetFile, keyHeader, newMetadata, serverMetadata, ignorePayloads: true, odinContext);
        }

        private async Task<SharedSecretEncryptedFileHeader> GetFileByGlobalTransitId(IDriveFileSystem fs, Guid driveId,
            Guid globalTransitId,
            IOdinContext odinContext)
        {
            var existingFile = await fs.Query.GetFileByGlobalTransitId(driveId, globalTransitId, odinContext);
            return existingFile;
        }
    }
}