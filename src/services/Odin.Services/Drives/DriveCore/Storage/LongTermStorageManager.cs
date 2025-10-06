using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Tasks;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Util;

namespace Odin.Services.Drives.DriveCore.Storage
{
    public class LongTermStorageManager(
        ILogger<LongTermStorageManager> logger,
        IPayloadReaderWriter payloadReaderWriter,
        DriveQuery driveQuery,
        ScopedIdentityTransactionFactory scopedIdentityTransactionFactory,
        TableDriveTransferHistory tableDriveTransferHistory,
        TableDriveMainIndexCached driveMainIndex,
        TenantContext tenantContext,
        IForgottenTasks forgottenTasks)
    {
        private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

        /// <summary>
        /// Creates an Id for storing a file
        /// </summary>
        /// <returns></returns>
        public Guid CreateFileId()
        {
            return SequentialGuid.CreateGuid();
        }

        /// <summary>
        /// Writes a file header to the database
        /// </summary>
        public async Task SaveFileHeader(StorageDrive drive, ServerFileHeader header, Guid? useThisVersionTag)
        {
            OdinValidationUtils.AssertNotNull(header, nameof(header));
            await driveQuery.SaveFileHeaderAsync(drive, header, useThisVersionTag);
        }

        public async Task SaveLocalMetadataAsync(InternalDriveFileId file, LocalAppMetadata metadata, Guid newVersionTag)
        {
            OdinValidationUtils.AssertIsTrue(file.IsValid(), "file is invalid");

            var json = OdinSystemSerializer.Serialize(metadata);
            await driveQuery.SaveLocalMetadataAsync(file.DriveId, file.FileId, metadata.VersionTag, json, newVersionTag);
        }

        public async Task SaveLocalMetadataTagsAsync(InternalDriveFileId file, LocalAppMetadata metadata, Guid newVersionTag)
        {
            OdinValidationUtils.AssertIsTrue(file.IsValid(), "file is invalid");
            await driveQuery.SaveLocalMetadataTagsAsync(file.DriveId, file.FileId, metadata, newVersionTag);
        }

        public async Task SoftDeleteFileHeader(ServerFileHeader header)
        {
            OdinValidationUtils.AssertNotNull(header, nameof(header));
            await driveQuery.SoftDeleteFileHeader(header);
        }

        public async Task<(RecipientTransferHistory updatedHistory, UnixTimeUtc modifiedTime)> InitiateTransferHistoryAsync(
            Guid driveId,
            Guid fileId,
            OdinId recipient)
        {
            logger.LogDebug("InitiateTransferHistoryAsync for file: {f} on drive: {d}", fileId, driveId);

            await using var tx = await scopedIdentityTransactionFactory.BeginStackedTransactionAsync();
            var added = await tableDriveTransferHistory.TryAddInitialRecordAsync(driveId, fileId, recipient);
            if (!added)
            {
                logger.LogDebug("InitiateTransferHistoryAsync: Insert failed, now updating for file: {f} on drive: {d}", fileId, driveId);

                var affectedRows = await tableDriveTransferHistory.UpdateTransferHistoryRecordAsync(driveId, fileId, recipient,
                    latestTransferStatus: null,
                    latestSuccessfullyDeliveredVersionTag: null,
                    isInOutbox: true,
                    isReadByRecipient: null);

                if (affectedRows != 1)
                {
                    throw new OdinSystemException($"Failed to initiate transfer history for recipient:{recipient}.  " +
                                                  $"Could not add or update transfer record");
                }
            }

            var (history, modified) = await UpdateTransferHistorySummary(driveId, fileId);

            tx.Commit();

            return (history, modified);
        }

        public async Task<(RecipientTransferHistory updatedHistory, UnixTimeUtc modifiedTime)> SaveTransferHistoryAsync(Guid driveId,
            Guid fileId, OdinId recipient,
            UpdateTransferHistoryData updateData)
        {
            OdinValidationUtils.AssertNotNull(updateData, nameof(updateData));

            logger.LogDebug("Begin Transaction for SaveTransferHistoryAsync file: {f}, driveId {d}. UpdateData: {u}", fileId, driveId,
                updateData.ToDebug());

            await using var tx = await scopedIdentityTransactionFactory.BeginStackedTransactionAsync();

            await tableDriveTransferHistory.UpdateTransferHistoryRecordAsync(driveId, fileId, recipient,
                (int?)updateData.LatestTransferStatus,
                updateData.VersionTag,
                updateData.IsInOutbox,
                updateData.IsReadByRecipient);

            var (history, modified) = await UpdateTransferHistorySummary(driveId, fileId);

            tx.Commit();

            logger.LogDebug("End Transaction for SaveTransferHistoryAsync file: {f}, driveId {d}", fileId, driveId);

            return (history, modified);
        }

        private async Task<(RecipientTransferHistory history, UnixTimeUtc modified)> UpdateTransferHistorySummary(Guid driveId,
            Guid fileId)
        {
            var fileTransferHistory = await GetTransferHistory(driveId, fileId);

            var history = new RecipientTransferHistory()
            {
                Summary = new TransferHistorySummary()
                {
                    TotalInOutbox = fileTransferHistory.Count(h => h.IsInOutbox),
                    TotalFailed = fileTransferHistory.Count(h => h.LatestTransferStatus != LatestTransferStatus.Delivered &&
                                                                 h.LatestTransferStatus != LatestTransferStatus.None),
                    TotalDelivered = fileTransferHistory.Count(h => h.LatestTransferStatus == LatestTransferStatus.Delivered),
                    TotalReadByRecipient = fileTransferHistory.Count(h => h.IsReadByRecipient)
                }
            };

            var json = OdinSystemSerializer.Serialize(history);

            var (_, modified) = await driveMainIndex.UpdateTransferSummaryAsync(driveId, fileId, json);

            // TODO: What if count is zero?

            return (history, new UnixTimeUtc(modified));
        }

        public async Task DeleteTransferHistoryAsync(StorageDrive drive, Guid fileId)
        {
            await tableDriveTransferHistory.DeleteAllRowsAsync(drive.Id, fileId);
        }

        public async Task SaveReactionHistory(StorageDrive drive, Guid fileId, ReactionSummary summary)
        {
            OdinValidationUtils.AssertNotNull(summary, nameof(summary));
            await driveQuery.SaveReactionSummary(drive, fileId, summary);
        }

        public async Task DeleteReactionSummary(StorageDrive drive, Guid fileId)
        {
            await driveQuery.SaveReactionSummary(drive, fileId, null);
        }
/*
        private void HardDeleteThumbnailFile(StorageDrive drive, Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid, int width, int height)
        {
            Benchmark.Milliseconds(logger, nameof(HardDeleteThumbnailFile), () =>
            {
                //var fileName = tenantPathManager.GetThumbnailFileName(fileId, payloadKey, payloadUid, width, height);
                //var dir =  GetPayloadDirectory(drive, fileId, FilePart.Thumb);
                //var path = Path.Combine(dir, fileName);

                var path = _tenantPathManager.GetThumbnailDirectoryAndFileName(drive.Id, fileId, payloadKey, payloadUid, width, height);

                //if (s != path)
                //{
                //    logger.LogError($"HardDeleteThumbnailFile {path} != {s}");
                //    Debug.Assert(s != path);
                //}

                driveFileReaderWriter.DeleteFile(path);
            });
        }
*/
        /// <summary>
        /// Deletes the payload file and all associated thumbnails
        /// </summary>
        private async Task HardDeletePayloadFileAsync(StorageDrive drive, Guid fileId, PayloadDescriptor payloadDescriptor)
        {
            string payloadKey = payloadDescriptor.Key;
            UnixTimeUtcUnique payloadUid = payloadDescriptor.Uid;
            
            var pathAndFilename = _tenantPathManager.GetPayloadDirectoryAndFileName(drive.Id, fileId, payloadKey, payloadUid);

            if (await payloadReaderWriter.FileExistsAsync(pathAndFilename))
            {
                await payloadReaderWriter.DeleteFileAsync(pathAndFilename);
            }
            else
            {
                logger.LogError("HardDeletePayloadFile -> source payload does not exist [{pathAndFilename}]", pathAndFilename);
            }

            foreach (var thumbnail in payloadDescriptor.Thumbnails)
            {
                //var thumbnailFileName = TenantPathManager.GetThumbnailFileName(fileId, payloadKey, payloadUid, thumbnail.PixelWidth, thumbnail.PixelHeight);
                //var dir = _tenantPathManager.GetPayloadDirectory(drive.Id, fileId);
                //var thumbnailFilenameAndPath = Path.Combine(dir, thumbnailFileName);
                var thumbnailFilenameAndPath = _tenantPathManager.GetThumbnailDirectoryAndFileName(drive.Id, fileId, payloadKey, payloadUid, thumbnail.PixelWidth, thumbnail.PixelHeight);

                if (await payloadReaderWriter.FileExistsAsync(thumbnailFilenameAndPath))
                {
                    await payloadReaderWriter.DeleteFileAsync(thumbnailFilenameAndPath);
                }
                else
                {
                    logger.LogError("HardDeletePayloadFile -> Renaming Thumbnail: source thumbnail does not exist [{thumbnailFile}]",
                        thumbnailFilenameAndPath);
                }
            }
        }

        public Task TryHardDeleteListOfPayloadFiles(StorageDrive drive, Guid fileId, List<PayloadDescriptor> descriptors)
        {
            return Task.Run(async () =>
            {
                if (drive.TargetDriveInfo == SystemDriveConstants.FeedDrive)
                {
                    return;
                }

                foreach (var descriptor in descriptors)
                {
                    try
                    {
                        await HardDeletePayloadFileAsync(drive, fileId, descriptor);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed while deleting a payload");
                    }
                }
            });
        }

        /// <summary>
        /// Removes all traces of a file and deletes its record from the index
        /// </summary>
        public async Task HardDeleteAsync(StorageDrive drive, Guid fileId, List<PayloadDescriptor> descriptors)
        {
            // First delete the DB header
            await driveQuery.HardDeleteFileHeaderAsync(drive, new InternalDriveFileId(drive, fileId));

            // If some files fail, they are simply orphaned
            forgottenTasks.Add(TryHardDeleteListOfPayloadFiles(drive, fileId, descriptors));
        }

        public async Task<bool> PayloadExistsOnDiskAsync(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor)
        {
            var path = _tenantPathManager.GetPayloadDirectoryAndFileName(drive.Id, fileId, descriptor.Key, descriptor.Uid);
            var exists = await payloadReaderWriter.FileExistsAsync(path);
            return exists;
        }

        public async Task<long> PayloadLengthAsync(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor)
        {
            var path = _tenantPathManager.GetPayloadDirectoryAndFileName(drive.Id, fileId, descriptor.Key, descriptor.Uid);
            var bytes = await payloadReaderWriter.FileLengthAsync(path);
            return bytes;
        }


        public async Task<bool> ThumbnailExistsOnDiskAsync(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor,
            ThumbnailDescriptor thumbnailDescriptor)
        {
            var path = _tenantPathManager.GetThumbnailDirectoryAndFileName(drive.Id, fileId, descriptor.Key, descriptor.Uid,
                thumbnailDescriptor.PixelWidth, thumbnailDescriptor.PixelHeight);

            return await payloadReaderWriter.FileExistsAsync(path);
        }

        public async Task<long> ThumbnailLengthAsync(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor, ThumbnailDescriptor thumbnailDescriptor)
        {
            var path = _tenantPathManager.GetThumbnailDirectoryAndFileName(drive.Id, fileId, descriptor.Key, descriptor.Uid, 
                thumbnailDescriptor.PixelWidth, thumbnailDescriptor.PixelHeight);
            var bytes = await payloadReaderWriter.FileLengthAsync(path);
            return bytes;
        }

        public async Task<Stream> GetPayloadStreamAsync(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor, FileChunk chunk = null)
        {
            var path = _tenantPathManager.GetPayloadDirectoryAndFileName(drive.Id, fileId, descriptor.Key, descriptor.Uid);

            if (chunk == null)
            {
                logger.LogDebug("GetPayloadStreamAsync: {path}", path);
                var bytes = await payloadReaderWriter.GetFileBytesAsync(path);
                return new MemoryStream(bytes);
            }
            else
            {
                logger.LogDebug("GetPayloadStreamAsync: {path}, start={start}, length={length}", path, chunk.Start, chunk.Length);
                var bytes = await payloadReaderWriter.GetFileBytesAsync(path, chunk.Start, chunk.Length);
                return new MemoryStream(bytes);
            }
        }


        /// <summary>
        /// Gets a read stream of the thumbnail
        /// </summary>
        public async Task<Stream> GetThumbnailStreamAsync(StorageDrive drive, Guid fileId, int width, int height, string payloadKey,
            UnixTimeUtcUnique payloadUid)
        {
            var fileName = TenantPathManager.GetThumbnailFileName(fileId, payloadKey, payloadUid, width, height);
            var dir = _tenantPathManager.GetPayloadDirectory(drive.Id, fileId);
            var path = Path.Combine(dir, fileName);

            try
            {
                var bytes = await payloadReaderWriter.GetFileBytesAsync(path);
                return new MemoryStream(bytes);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to get thumbnail stream for file {path}", path);
                throw;
            }
        }

        public async Task<List<RecipientTransferHistoryItem>> GetTransferHistory(Guid driveId, Guid fileId)
        {
            var list = await tableDriveTransferHistory.GetAsync(driveId, fileId);
            return list.Select(item => new RecipientTransferHistoryItem
                {
                    Recipient = item.remoteIdentityId,
                    LastUpdated = default,
                    LatestTransferStatus = (LatestTransferStatus)item.latestTransferStatus,
                    IsInOutbox = item.isInOutbox,
                    LatestSuccessfullyDeliveredVersionTag = item.latestSuccessfullyDeliveredVersionTag,
                    IsReadByRecipient = item.isReadByRecipient
                }
            ).ToList();
        }


        /// <summary>
        /// Checks if the header file exists in db.  Does not check the validity of the header
        /// </summary>
        public async Task<bool> HeaderFileExists(StorageDrive drive, Guid fileId, FileSystemType fileSystemType)
        {
            var header = await this.GetServerFileHeader(drive, fileId, fileSystemType);
            if (header == null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Moves the specified <param name="sourceFile"></param> to long term storage.
        /// Returns the full path of the desitnation file.
        /// </summary>
        public async Task CopyPayloadToLongTermAsync(StorageDrive drive, Guid targetFileId, PayloadDescriptor descriptor, string sourceFile)
        {
            var destinationFile = _tenantPathManager.GetPayloadDirectoryAndFileName(
                drive.Id,
                targetFileId,
                descriptor.Key,
                descriptor.Uid);

            await payloadReaderWriter.CopyPayloadFileAsync(sourceFile, destinationFile);

            logger.LogDebug("Payload: copied {sourceFile} to {destinationFile}", sourceFile, destinationFile);
        }
        
        public async Task CopyThumbnailToLongTermAsync(StorageDrive drive, Guid targetFileId, string sourceThumbnailFilePath,
            PayloadDescriptor payloadDescriptor,
            ThumbnailDescriptor thumbnailDescriptor)
        {
            var payloadKey = payloadDescriptor.Key;

            TenantPathManager.AssertValidPayloadKey(payloadKey);
            var destinationFile = _tenantPathManager.GetThumbnailDirectoryAndFileName(drive.Id, targetFileId, payloadKey,
                payloadDescriptor.Uid,
                thumbnailDescriptor.PixelWidth, thumbnailDescriptor.PixelHeight);

            var dir = Path.GetDirectoryName(destinationFile) ??
                      throw new OdinSystemException("Destination folder was null");
            logger.LogInformation("Creating Directory for thumbnail: {dir}", dir);

            await payloadReaderWriter.CopyPayloadFileAsync(sourceThumbnailFilePath, destinationFile);
            logger.LogDebug("Thumbnail: moved {sourceThumbnailFilePath} to {destinationFile}",
                sourceThumbnailFilePath, destinationFile);

        }

        public async Task<ServerFileHeader> GetServerFileHeader(StorageDrive drive, Guid fileId, FileSystemType fileSystemType)
        {
            var header = await driveQuery.GetFileHeaderAsync(drive, fileId, fileSystemType);
            return header;
        }
    }
}