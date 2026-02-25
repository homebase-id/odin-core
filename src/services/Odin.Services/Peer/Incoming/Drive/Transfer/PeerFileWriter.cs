using System;
using System.Collections.Generic;
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
using Odin.Services.DataSubscription;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;

namespace Odin.Services.Peer.Incoming.Drive.Transfer
{
    //TODO: this should be split into a file writer for comments and a file writer for standard files.

    /// <summary>
    /// Handles the process of writing a file from temp storage to long-term storage
    /// </summary>
    public class PeerFileWriter(ILogger logger, FileSystemResolver fileSystemResolver, IDriveManager driveManager, FeedWriter feedWriter)
    {
        public async Task<(bool success, List<PayloadDescriptor> payloads)> HandleFile(InboxFile tempFile,
            IDriveFileSystem fs,
            KeyHeader decryptedKeyHeader,
            OdinId sender,
            EncryptedRecipientTransferInstructionSet encryptedRecipientTransferInstructionSet,
            IOdinContext odinContext,
            bool driveOriginWasCollaborative = false,
            WriteSecondDatabaseRowBase markComplete = null)
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
                    logger.LogError("Cannot find the metadata file (File:{file} on DriveId:{driveID}) was not found ", tempFile.FileId.FileId,
                        tempFile.FileId.DriveId);
                    throw new OdinFileWriteException("Missing temp file while processing inbox");
                }

                string json = bytes.ToStringFromUtf8Bytes();

                metadata = OdinSystemSerializer.Deserialize<FileMetadata>(json);

                // var theDrive = await driveManager.GetDrive(tempFile.DriveId);
                if (null == metadata)
                {
                    logger.LogError("Metadata file (File:{file} on DriveId:{driveID}) could not be deserialized ",
                        tempFile.FileId.FileId,
                        tempFile.FileId.DriveId);
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

            var drive = await driveManager.GetDriveAsync(tempFile.FileId.DriveId);
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
                    return await StoreNormalFileLongTermAsync(fs, tempFile, decryptedKeyHeader, metadata, serverMetadata,
                        encryptedRecipientTransferInstructionSet, odinContext, markComplete);

                case TransferFileType.EncryptedFileForFeed:
                case TransferFileType.EncryptedFileForFeedViaTransit:
                    return await StoreEncryptedFeedFile(fs, tempFile, decryptedKeyHeader, metadata,
                        driveOriginWasCollaborative,
                        odinContext, markComplete);

                default:
                    throw new OdinFileWriteException($"Invalid TransferFileType: {transferFileType}");
            }
        }

        public async Task<bool> DeleteFile(IDriveFileSystem fs, TransferInboxItem item, IOdinContext odinContext,
            WriteSecondDatabaseRowBase markComplete)
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

            return await fs.Storage.SoftDeleteLongTermFile(file, odinContext, markComplete);
        }

        public async Task<bool> MarkFileAsRead(IDriveFileSystem fs, TransferInboxItem item, IOdinContext odinContext,
            WriteSecondDatabaseRowBase markComplete)
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

            return await fs.Storage.UpdateTransferHistory(
                file,
                item.Sender,
                update,
                odinContext,
                markComplete);
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

        private async Task<(bool success, List<PayloadDescriptor> payloads)> WriteNewFile(IDriveFileSystem fs, InboxFile tempFile,
            KeyHeader keyHeader,
            FileMetadata metadata, ServerMetadata serverMetadata, bool ignorePayloads, IOdinContext odinContext,
            WriteSecondDatabaseRowBase markComplete)
        {
            bool success = false;
            List<PayloadDescriptor> payloads = [];
            var ms = await PerformanceCounter.MeasureExecutionTime("PeerFileWriter WriteNewFile", async () =>
            {
                metadata.TransitCreated = UnixTimeUtc.Now().milliseconds;
                (success, payloads) = await fs.Storage.CommitNewFile(tempFile, keyHeader, metadata,
                    serverMetadata, ignorePayloads, odinContext, markComplete: markComplete);
            });

            logger.LogDebug("Handle file->CommitNewFile: {ms} ms", ms);
            return (success, payloads);
        }


        private async Task<(bool success, List<PayloadDescriptor> payloads)> UpdateExistingFile(IDriveFileSystem fs,
            InboxFile tempSourceFile, InternalDriveFileId targetFile,
            KeyHeader keyHeader,
            FileMetadata metadata, ServerMetadata serverMetadata, bool ignorePayloads, IOdinContext odinContext,
            WriteSecondDatabaseRowBase markComplete)
        {
            bool success = false;
            List<PayloadDescriptor> payloads = [];
            await PerformanceCounter.MeasureExecutionTime("PeerFileWriter UpdateExistingFile", async () =>
            {
                //Use the version tag from the recipient's server because it won't match the sender (this is due to the fact a new
                //one is written any time you save a header)
                metadata.TransitUpdated = UnixTimeUtc.Now().milliseconds;
                //note: we also update the key header because it might have been changed by the sender

                (success, payloads) = await fs.Storage.OverwriteFile(tempSourceFile, targetFile, keyHeader, metadata, serverMetadata,
                    ignorePayloads,
                    odinContext, markComplete);
            });

            return (success, payloads);
        }

        /// <summary>
        /// Stores a long-term file or overwrites an existing long-term file if a global transit id was set
        /// </summary>
        private async Task<(bool success, List<PayloadDescriptor> payloads)> StoreNormalFileLongTermAsync(IDriveFileSystem fs,
            InboxFile tempFile, KeyHeader keyHeader,
            FileMetadata newMetadata, ServerMetadata serverMetadata,
            EncryptedRecipientTransferInstructionSet encryptedRecipientTransferInstructionSet, IOdinContext odinContext,
            WriteSecondDatabaseRowBase markComplete)
        {
            var ignorePayloads = newMetadata.PayloadsAreRemote;
            var targetDriveId = tempFile.FileId.DriveId;

            if (newMetadata.GlobalTransitId.HasValue == false)
            {
                throw new OdinClientException("Must have a global transit id to write peer.", OdinClientErrorCode.InvalidFile);
            }

            // First we check if we can match the gtid to an existing file on disk.
            // If we can, then the gtid is the winner and decides the matching file
            //

            SharedSecretEncryptedFileHeader header = await GetFileByGlobalTransitId(fs, tempFile.FileId.DriveId,
                newMetadata.GlobalTransitId.GetValueOrDefault(), odinContext);

            // If there is no file matching the gtid, let's check if the UID might point to one
            if (header == null && newMetadata.AppData.UniqueId.HasValue)
            {
                header = await fs.Query.GetFileByClientUniqueIdForWriting(targetDriveId, newMetadata.AppData.UniqueId.Value, odinContext);
            }

            if (header == null)
            {
                // Neither gtid not uid points to an exiting file, so it's a new file
                return await WriteNewFile(fs, tempFile, keyHeader, newMetadata, serverMetadata, ignorePayloads, odinContext, markComplete);
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
            return await UpdateExistingFile(fs, tempFile, targetFile, keyHeader, newMetadata, serverMetadata, ignorePayloads, odinContext,
                markComplete);
        }


        private async Task<(bool success, List<PayloadDescriptor> payloads)> StoreEncryptedFeedFile(IDriveFileSystem fs, InboxFile tempFile,
            KeyHeader keyHeader,
            FileMetadata newMetadata, bool driveOriginWasCollaborative, IOdinContext odinContext,
            WriteSecondDatabaseRowBase markComplete)
        {
            // Rules:
            // You must have a global transit id to write to the feed drive
            // We never allow uniqueId for the feed drive
            newMetadata.AppData.UniqueId = null;

            if (newMetadata.GlobalTransitId.HasValue == false)
            {
                throw new OdinClientException("Must have a global transit id to write to the feed drive.", OdinClientErrorCode.InvalidFile);
            }

            var header = await GetFileByGlobalTransitId(fs, tempFile.FileId.DriveId, newMetadata.GlobalTransitId.GetValueOrDefault(),
                odinContext);


            if (header == null)
            {
                await feedWriter.WriteNewFileToFeedDriveAsync(keyHeader, newMetadata, odinContext);

                logger.LogDebug("{method} -> markComplete {message}", 
                    nameof(StoreEncryptedFeedFile),
                    markComplete == null ? "is not configured" : "will be called");
                
                if (markComplete != null)
                {
                    int n = await markComplete.ExecuteAsync();
                    if (n != 1)
                        throw new OdinSystemException("Hum, unable to mark the inbox record as completed, aborting");
                }

                return (success: true, payloads: []);
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
                DriveId = SystemDriveConstants.FeedDrive.Alias
            };

            //Update the reaction preview first since the overwrite method; uses what's on disk
            // we call both of these here because this 'special' feed item hack method for collabgroups
            await fs.Storage.UpdateReactionSummary(targetFile, newMetadata.ReactionPreview,
                odinContext); // XXX Ideally this should be part of the DB transaction... but alas! 

            await feedWriter.ReplaceFileMetadataOnFeedDrive(header.FileId,
                newMetadata,
                odinContext,
                bypassCallerCheck: driveOriginWasCollaborative, // the caller will not be original sender in the case of a collab drive 
                keyHeader: keyHeader);

            logger.LogDebug("{method} -> markComplete {message}", 
                nameof(StoreEncryptedFeedFile),
                markComplete == null ? "is not configured" : "will be called");
            
            //note: we also update the key header because it might have been changed by the sender
            if (markComplete != null)
            {
                int n = await markComplete.ExecuteAsync();
                if (n != 1)
                    throw new OdinSystemException("Hum, unable to mark the inbox record as completed, aborting");
            }

            return (success: true, payloads: []);

            // return await UpdateExistingFile(fs, tempFile, targetFile, keyHeader, newMetadata, serverMetadata, ignorePayloads: true,
            //     odinContext, markComplete);
        }

        private async Task<SharedSecretEncryptedFileHeader> GetFileByGlobalTransitId(IDriveFileSystem fs, Guid driveId,
            Guid globalTransitId,
            IOdinContext odinContext)
        {
            var existingFile = await fs.Query.GetFileByGlobalTransitId(driveId, globalTransitId, odinContext);
            return existingFile;
        }

        public async Task CleanupInboxFiles(InboxFile tempFile, List<PayloadDescriptor> payloads, IOdinContext odinContext)
        {
            var fs = fileSystemResolver.ResolveFileSystem(FileSystemType.Standard);
            await fs.Storage.CleanupInboxTemporaryFiles(tempFile, payloads, odinContext);
        }
    }
}