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
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Util;

namespace Odin.Services.Drives.DriveCore.Storage
{
    public class LongTermStorageManager(
        ILogger<LongTermStorageManager> logger,
        DriveFileReaderWriter driveFileReaderWriter,
        DriveQuery driveQuery,
        ScopedIdentityTransactionFactory scopedIdentityTransactionFactory,
        TableDriveTransferHistory tableDriveTransferHistory,
        TableDriveMainIndex driveMainIndex,
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
        private void HardDeletePayloadFile(StorageDrive drive, Guid fileId, PayloadDescriptor payloadDescriptor)
        {
            string payloadKey = payloadDescriptor.Key;
            UnixTimeUtcUnique payloadUid = payloadDescriptor.Uid;
            
            var pathAndFilename = _tenantPathManager.GetPayloadDirectoryAndFileName(drive.Id, fileId, payloadKey, payloadUid);

            if (driveFileReaderWriter.FileExists(pathAndFilename))
            {
                driveFileReaderWriter.DeleteFile(pathAndFilename);
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

                if (driveFileReaderWriter.FileExists(thumbnailFilenameAndPath))
                {
                    driveFileReaderWriter.DeleteFile(thumbnailFilenameAndPath);
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
            return Task.Run(() =>
            {
                if (drive.TargetDriveInfo == SystemDriveConstants.FeedDrive)
                {
                    return;
                }

                foreach (var descriptor in descriptors)
                {
                    try
                    {
                        HardDeletePayloadFile(drive, fileId, descriptor);
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

        public bool PayloadExistsOnDisk(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor)
        {
            var path = _tenantPathManager.GetPayloadDirectoryAndFileName(drive.Id, fileId, descriptor.Key, descriptor.Uid);
            var exists = driveFileReaderWriter.FileExists(path);
            return exists;
        }

        public bool ThumbnailExistsOnDisk(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor,
            ThumbnailDescriptor thumbnailDescriptor)
        {
            var path = _tenantPathManager.GetThumbnailDirectoryAndFileName(drive.Id, fileId, descriptor.Key, descriptor.Uid,
                thumbnailDescriptor.PixelWidth, thumbnailDescriptor.PixelHeight);

            return driveFileReaderWriter.FileExists(path);
        }

        public async Task<Stream> GetPayloadStream(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor, FileChunk chunk = null)
        {
            var result = await Benchmark.MillisecondsAsync(logger, "GetPayloadStream", async () => await Execute());
            return result;

            async Task<Stream> Execute()
            {
                var path = _tenantPathManager.GetPayloadDirectoryAndFileName(drive.Id, fileId, descriptor.Key, descriptor.Uid);
                logger.LogDebug("Get Chunked Stream called on file [{path}]", path);

                Stream fileStream;
                try
                {
                    fileStream = driveFileReaderWriter.OpenStreamForReading(path);
                    logger.LogDebug("File size: {size} bytes", fileStream.Length);
                }
                catch (IOException io)
                {
                    if (io is FileNotFoundException || io is DirectoryNotFoundException)
                    {
                        throw new OdinFileHeaderHasCorruptPayloadException(
                            $"Missing payload file [path:{path}] for key {descriptor.Key} with uid: {descriptor.Uid.uniqueTime}");
                    }

                    throw;
                }

                if (null != chunk)
                {
                    try
                    {
                        var buffer = new byte[Math.Min(chunk.Length, fileStream.Length)];
                        if (chunk.Start > fileStream.Length)
                        {
                            throw new OdinClientException("Chunk start position is greater than length",
                                OdinClientErrorCode.InvalidChunkStart);
                        }

                        fileStream.Position = chunk.Start;
                        var bytesRead = fileStream.Read(buffer);

                        //resize if length requested was too large (happens if we hit the end of the stream)
                        if (bytesRead < buffer.Length)
                        {
                            Array.Resize(ref buffer, bytesRead);
                        }

                        // return Task.FromResult((Stream)new MemoryStream(buffer, false));
                        return new MemoryStream(buffer, false);
                    }
                    finally
                    {
                        await fileStream.DisposeAsync();
                    }
                }

                return fileStream;
            }
        }


        /// <summary>
        /// Gets a read stream of the thumbnail
        /// </summary>
        public Stream GetThumbnailStream(StorageDrive drive, Guid fileId, int width, int height, string payloadKey,
            UnixTimeUtcUnique payloadUid)
        {
            var result = Benchmark.Milliseconds(logger, "GetThumbnailStream", () =>
            {
                var fileName = TenantPathManager.GetThumbnailFileName(fileId, payloadKey, payloadUid, width, height);
                var dir = _tenantPathManager.GetPayloadDirectory(drive.Id, fileId);
                var path = Path.Combine(dir, fileName);

                try
                {
                    var fileStream = driveFileReaderWriter.OpenStreamForReading(path);
                    return fileStream;
                }
                catch (IOException io)
                {
                    if (io is FileNotFoundException || io is DirectoryNotFoundException)
                    {
                        throw new OdinFileHeaderHasCorruptPayloadException(
                            $"Missing thumbnail file [path:{path}] for key {payloadKey} with uid: {payloadUid.uniqueTime}");
                    }

                    throw;
                }
            });
            return result;
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
        /// Returns the storage UID used in the filename
        /// </summary>
        public void CopyPayloadToLongTerm(StorageDrive drive, Guid targetFileId, PayloadDescriptor descriptor, string sourceFile)
        {
            Benchmark.Milliseconds(logger, nameof(CopyPayloadToLongTerm), () =>
            {
                var destinationFile = _tenantPathManager.GetPayloadDirectoryAndFileName(drive.Id, targetFileId, descriptor.Key,
                    descriptor.Uid, ensureExists: true);
                driveFileReaderWriter.CopyPayloadFile(sourceFile, destinationFile);
                logger.LogDebug("Payload: copied {sourceFile} to {destinationFile}", sourceFile, destinationFile);
            });
        }

        public void CopyThumbnailToLongTerm(StorageDrive drive, Guid targetFileId, string sourceThumbnailFilePath,
            PayloadDescriptor payloadDescriptor,
            ThumbnailDescriptor thumbnailDescriptor)
        {
            Benchmark.Milliseconds(logger, "MoveThumbnailToLongTerm", () =>
            {
                var payloadKey = payloadDescriptor.Key;

                TenantPathManager.AssertValidPayloadKey(payloadKey);
                var destinationFile = _tenantPathManager.GetThumbnailDirectoryAndFileName(drive.Id, targetFileId, payloadKey,
                    payloadDescriptor.Uid,
                    thumbnailDescriptor.PixelWidth, thumbnailDescriptor.PixelHeight);

                var dir = Path.GetDirectoryName(destinationFile) ??
                          throw new OdinSystemException("Destination folder was null");
                logger.LogInformation("Creating Directory for thumbnail: {dir}", dir);
                driveFileReaderWriter.CreateDirectory(dir); // TODO REMOVE

                driveFileReaderWriter.CopyPayloadFile(sourceThumbnailFilePath, destinationFile);
                logger.LogDebug("Thumbnail: moved {sourceThumbnailFilePath} to {destinationFile}",
                    sourceThumbnailFilePath, destinationFile);
            });
        }

        public async Task<ServerFileHeader> GetServerFileHeader(StorageDrive drive, Guid fileId, FileSystemType fileSystemType)
        {
            var header = await driveQuery.GetFileHeaderAsync(drive, fileId, fileSystemType);
            return header;
        }
    }
}