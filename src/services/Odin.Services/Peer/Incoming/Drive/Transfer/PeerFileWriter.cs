using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
        public async Task<(bool success, List<PayloadDescriptor> payloads)> HandleFile(InternalDriveFileId file,
            IDriveFileSystem fs,
            KeyHeader decryptedKeyHeader,
            OdinId sender,
            EncryptedRecipientTransferInstructionSet encryptedRecipientTransferInstructionSet,
            IOdinContext odinContext,
            StagingArea sourceArea,
            bool driveOriginWasCollaborative = false,
            WriteSecondDatabaseRowBase markComplete = null,
            FileMetadata metadataOverride = null)
        {
            var fileSystemType = encryptedRecipientTransferInstructionSet.FileSystemType;
            var transferFileType = encryptedRecipientTransferInstructionSet.TransferFileType;

            var drive = await driveManager.GetDriveAsync(file.DriveId);

            // TODO:INBOX Delete this fallback block (keep only metadata = metadataOverride) once no pre-upgrade
            // peer-transfer items remain in any inbox.
            // New inbox items carry the metadata on the inbox row (metadataOverride); they staged nothing on disk.
            // Legacy/in-flight items (queued before the metadata moved onto the row) still have a .metadata file in
            // the staging folder, so fall back to reading it. This is the dual-read transition.
            FileMetadata metadata = metadataOverride;
            if (metadata == null)
            {
                var metadataMs = await PerformanceCounter.MeasureExecutionTime("PeerFileWriter HandleFile ReadTempFile", async () =>
                {
                    await using var stream = await fs.Storage.OpenTempFileForReadingAsync(file,
                        MultipartHostTransferParts.Metadata.ToString().ToLower(), sourceArea, odinContext);

                    metadata = await OdinSystemSerializer.Deserialize<FileMetadata>(stream);

                    if (null == metadata)
                    {
                        logger.LogError("Metadata file (File:{file} on DriveId:{driveID}) could not be deserialized ",
                            file.FileId,
                            file.DriveId);
                        throw new OdinFileWriteException("Metadata could not be deserialized");
                    }
                });

                logger.LogDebug("Get metadata from temp file and deserialize: {ms} ms", metadataMs);
            }

            // Files coming from other systems are only accessible to the owner so
            // the owner can use the UI to pass the file along
            var targetAcl = AccessControlList.OwnerOnly;

            var isCollaborationChannel = drive.IsCollaborationDrive();

            //TODO: this might be a hacky place to put this but let's let it cook.  It might better be put into the comment storage
            if (fileSystemType == FileSystemType.Comment)
            {
                targetAcl = await IncomingCommentAcl.ResetAclForComment(fileSystemResolver, metadata, odinContext);
            }
            else
            {
                //
                // Collab channel hack
                //
                if (isCollaborationChannel)
                {
                    targetAcl = encryptedRecipientTransferInstructionSet.OriginalAcl ?? AccessControlList.OwnerOnly;
                }

                // A member's new file, shared with their circles
                if (drive.IsSelectiveShare && encryptedRecipientTransferInstructionSet.Shares is { Count: > 0 })
                {
                    SelectiveShareDrives.LiftForShares(targetAcl);
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
                    return await StoreNormalFileLongTermAsync(fs, file, decryptedKeyHeader, metadata, serverMetadata,
                        encryptedRecipientTransferInstructionSet, odinContext, sourceArea, markComplete);

                case TransferFileType.EncryptedFileForFeed:
                case TransferFileType.EncryptedFileForFeedViaTransit:
                    return await StoreEncryptedFeedFile(fs, file, decryptedKeyHeader, metadata,
                        driveOriginWasCollaborative,
                        odinContext, markComplete);

                default:
                    throw new OdinFileWriteException($"Invalid TransferFileType: {transferFileType}");
            }
        }

        public async Task<bool> DeleteFile(IDriveFileSystem fs, TransferInboxItem item, IOdinContext odinContext,
            WriteSecondDatabaseRowBase markComplete)
        {
            logger.LogDebug("[DeleteFlow] PeerFileWriter.DeleteFile -> looking up gtid:{gtid} driveId:{driveId} sender:{sender}",
                item.GlobalTransitId, item.DriveId, item.Sender);

            var clientFileHeader = await fs.Query.GetFileByGlobalTransitId(item.DriveId, item.GlobalTransitId, odinContext);

            if (clientFileHeader == null)
            {
                // Benign: a delete request for a file we don't have (already deleted, or never
                // received). It's an idempotent no-op, not an error. Log at warning; the
                // OdinFileWriteException is handled in PeerInboxProcessor, which drops the item from
                // the inbox (DeleteFromInbox).
                logger.LogWarning(
                    "[DeleteFlow] PeerFileWriter.DeleteFile -> Cannot find the metadata file (global transit id:{globalTransitId} on DriveId:{driveId}) was not found",
                    item.GlobalTransitId, item.DriveId);
                throw new OdinFileWriteException("Missing file by global transit id while file while processing delete request in inbox");
            }

            var file = new InternalDriveFileId()
            {
                FileId = clientFileHeader.FileId,
                DriveId = item.DriveId,
            };

            logger.LogDebug("[DeleteFlow] PeerFileWriter.DeleteFile -> resolved fileId:{fileId} for gtid:{gtid}; soft-deleting",
                file.FileId, item.GlobalTransitId);

            var result = await fs.Storage.SoftDeleteLongTermFile(file, odinContext, markComplete) != null;

            logger.LogDebug("[DeleteFlow] PeerFileWriter.DeleteFile -> soft-delete returned {result} for fileId:{fileId} gtid:{gtid}",
                result, file.FileId, item.GlobalTransitId);

            return result;
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
                ReadByRecipientTimestamp = UnixTimeUtc.Now().milliseconds,
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

        private async Task<(bool success, List<PayloadDescriptor> payloads)> WriteNewFile(IDriveFileSystem fs, InternalDriveFileId file,
            KeyHeader keyHeader,
            FileMetadata metadata, ServerMetadata serverMetadata, bool ignorePayloads, IOdinContext odinContext,
            StagingArea sourceArea, WriteSecondDatabaseRowBase markComplete)
        {
            bool success = false;
            List<PayloadDescriptor> payloads = [];
            var ms = await PerformanceCounter.MeasureExecutionTime("PeerFileWriter WriteNewFile", async () =>
            {
                metadata.TransitCreated = UnixTimeUtc.Now().milliseconds;
                // Its sender's write, which their next change goes by (docs/file-access.md, "Changing a file you sent")
                var versionTag = DriveFileUtility.CreateVersionTag();
                serverMetadata.SendersWrite = SentFileChanges.SendersWriteOf((OdinId)metadata.SenderOdinId, metadata.VersionTag.GetValueOrDefault(),
                    versionTag);
                (success, payloads) = await fs.Storage.CommitNewFile(file, keyHeader, metadata,
                    serverMetadata, ignorePayloads, odinContext, useThisVersionTag: versionTag, markComplete: markComplete,
                    sourceArea: sourceArea);
            });

            logger.LogDebug("Handle file->CommitNewFile: {ms} ms", ms);
            return (success, payloads);
        }


        private async Task<(bool success, List<PayloadDescriptor> payloads)> UpdateExistingFile(IDriveFileSystem fs,
            InternalDriveFileId sourceFile, InternalDriveFileId targetFile,
            KeyHeader keyHeader,
            FileMetadata metadata, ServerMetadata serverMetadata, bool ignorePayloads, IOdinContext odinContext,
            StagingArea sourceArea, WriteSecondDatabaseRowBase markComplete, Guid versionTag)
        {
            bool success = false;
            List<PayloadDescriptor> payloads = [];
            await PerformanceCounter.MeasureExecutionTime("PeerFileWriter UpdateExistingFile", async () =>
            {
                metadata.TransitUpdated = UnixTimeUtc.Now().milliseconds;
                //note: we also update the key header because it might have been changed by the sender

                (success, payloads) = await fs.Storage.OverwriteFile(sourceFile, targetFile, keyHeader, metadata, serverMetadata,
                    ignorePayloads,
                    odinContext, markComplete, sourceArea: sourceArea, useThisVersionTag: versionTag);
            });

            return (success, payloads);
        }

        /// <summary>
        /// Stores a long-term file or overwrites an existing long-term file if a global transit id was set
        /// </summary>
        private async Task<(bool success, List<PayloadDescriptor> payloads)> StoreNormalFileLongTermAsync(IDriveFileSystem fs,
            InternalDriveFileId file, KeyHeader keyHeader,
            FileMetadata newMetadata, ServerMetadata serverMetadata,
            EncryptedRecipientTransferInstructionSet encryptedRecipientTransferInstructionSet, IOdinContext odinContext,
            StagingArea sourceArea, WriteSecondDatabaseRowBase markComplete)
        {
            var ignorePayloads = newMetadata.PayloadsAreRemote;
            var targetDriveId = file.DriveId;

            if (newMetadata.GlobalTransitId.HasValue == false)
            {
                throw new OdinClientException("Must have a global transit id to write peer.", OdinClientErrorCode.InvalidFile);
            }

            // A file sent again changes it, found by the server, not as the sender, who may only write it
            // (docs/file-access.md, "Changing a file you sent").  A comment is its author's to send again
            // (DriveStorageServiceBase checks the author and the share)
            var drive = await driveManager.GetDriveAsync(targetDriveId);
            var stored = await fs.Query.GetServerFileHeaderByGlobalTransitIdSkippingFileAcl(targetDriveId,
                             newMetadata.GlobalTransitId.GetValueOrDefault(), odinContext)
                         ?? (newMetadata.AppData.UniqueId is { } uniqueId
                             ? await fs.Query.GetServerFileHeaderByClientUniqueIdSkippingFileAcl(targetDriveId, uniqueId, odinContext)
                             : null);

            if (stored == null)
            {
                return await WriteNewFile(fs, file, keyHeader, newMetadata, serverMetadata, ignorePayloads, odinContext, sourceArea,
                    markComplete);
            }

            var sender = (OdinId)newMetadata.SenderOdinId;
            var (writeContext, sentIt) = await SentFileChanges.ContextForChangeAsync(fs, drive, stored, sender, memberMayUseShares: false,
                odinContext);
            var sendersVersionTag = newMetadata.VersionTag.GetValueOrDefault();
            if (SentFileChanges.AlreadyLandedOrAssertMayChange(stored, sender, sentIt, sendersVersionTag,
                    encryptedRecipientTransferInstructionSet.ExpectedVersionTag))
            {
                return await SentFileChanges.AlreadyDeliveredAsync(fs, file, newMetadata.Payloads, markComplete, odinContext);
            }

            // Shares sent with it would be checked as a new file's and written to the incoming file id
            if (encryptedRecipientTransferInstructionSet.Shares is { Count: > 0 })
            {
                throw new OdinClientException("A file sent again keeps its shares: a member sends shares only with a new file",
                    OdinClientErrorCode.ArgumentError);
            }

            // What an update keeps: encrypted or not, its data source, and a client-locked file's key header with a new IV
            DriveStorageServiceBase.AssertSameEncryption(stored, newMetadata);
            DriveStorageServiceBase.AssertSameDataSource(stored, newMetadata);
            if (newMetadata.IsEncrypted)
            {
                ZkDriveFiles.AssertFitsUpdate(stored, keyHeader);
            }

            // The tag the header write replaces, which its SQL checks again, and the one it gives the file
            newMetadata.VersionTag = stored.FileMetadata.VersionTag;
            var versionTag = DriveFileUtility.CreateVersionTag();
            serverMetadata.SendersWrite = SentFileChanges.SendersWriteOf(sender, sendersVersionTag, versionTag);

            //note: we also update the key header because it might have been changed by the sender
            return await UpdateExistingFile(fs, file, stored.FileMetadata.File, keyHeader, newMetadata, serverMetadata, ignorePayloads,
                writeContext, sourceArea, markComplete, versionTag);
        }


        private async Task<(bool success, List<PayloadDescriptor> payloads)> StoreEncryptedFeedFile(IDriveFileSystem fs, InternalDriveFileId file,
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

            var header = await fs.Query.GetFileByGlobalTransitId(file.DriveId, newMetadata.GlobalTransitId.GetValueOrDefault(), odinContext);


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
                DriveId = WellKnownAppDrives.FeedDrive.Alias
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
    }
}