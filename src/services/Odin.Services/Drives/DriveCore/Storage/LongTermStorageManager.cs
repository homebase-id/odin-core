using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Util;

namespace Odin.Services.Drives.DriveCore.Storage
{
    public class LongTermStorageManager(
        ILogger<LongTermStorageManager> logger,
        DriveFileReaderWriter driveFileReaderWriter,
        DriveQuery driveQuery,
        ScopedIdentityTransactionFactory scopedIdentityTransactionFactory,
        TableDriveTransferHistory tableDriveTransferHistory,
        DriveManager driveManager,
        TableDriveMainIndex driveMainIndex,
        TenantContext tenantContext)
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

        public void HardDeleteThumbnailFile(StorageDrive drive, Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid, int width, int height)
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

        /// <summary>
        /// Deletes the payload file and all associated thumbnails
        /// </summary>
        public void HardDeletePayloadFile(StorageDrive drive, Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid)
        {
            Benchmark.Milliseconds(logger, nameof(HardDeletePayloadFile), () =>
            {
                var pathAndFilename = _tenantPathManager.GetPayloadDirectoryAndFileName(drive.Id, fileId, payloadKey, payloadUid);

                //var s = tenantPathManager.GetPayloadDirectoryAndFileName(drive.Id, fileId, payloadKey, payloadUid);

                //if (s != pathAndFilename)
                //{
                //    logger.LogError($"HardDeleteThumbnailFile {pathAndFilename} != {s}");
                //    Debug.Assert(s != pathAndFilename);
                //}

                //
                // Re-enable DELETION this after we are good with actually deleting the file
                //

                // _driveFileReaderWriter.DeleteFile(path);

                var target = pathAndFilename.Replace(".payload", TenantPathManager.DeletePayloadExtension);
                logger.LogDebug("HardDeletePayloadFile -> attempting to rename [{source}] to [{dest}]",
                    pathAndFilename,
                    target);

                if (driveFileReaderWriter.FileExists(pathAndFilename))
                {
                    driveFileReaderWriter.MoveFile(pathAndFilename, target);
                }
                else
                {
                    logger.LogError("HardDeletePayloadFile -> source payload does not exist [{pathAndFilename}]", pathAndFilename);
                }

                // delete the thumbnails
                // _driveFileReaderWriter.DeleteFilesInDirectory(dir, thumbnailSearchPattern);

                // 1fedce18c0022900efbb396f9796d3d0-prfl_pic-113599297775861760-500x500.thumb
                var thumbnailSearchPattern = GetThumbnailSearchMask(fileId, payloadKey, payloadUid);
                var dir = _tenantPathManager.GetPayloadDirectory(drive.Id, fileId);
                var thumbnailFiles = driveFileReaderWriter.GetFilesInDirectory(dir, thumbnailSearchPattern);
                foreach (var thumbnailFile in thumbnailFiles)
                {
                    var thumbnailTarget = thumbnailFile.Replace(".thumb", TenantPathManager.DeletedThumbExtension);

                    if (driveFileReaderWriter.FileExists(thumbnailFile))
                    {
                        driveFileReaderWriter.MoveFile(thumbnailFile, thumbnailTarget);
                    }
                    else
                    {
                        logger.LogError("HardDeletePayloadFile -> Renaming Thumbnail: source thumbnail does not exist [{thumbnailFile}]",
                            thumbnailFile);
                    }
                }
            });
        }

        public void HardDeleteAllPayloadFiles(StorageDrive drive, Guid fileId)
        {
            Benchmark.Milliseconds(logger, nameof(HardDeleteAllPayloadFiles), () =>
            {
                var fn = TenantPathManager.GuidToPathSafeString(fileId);
                var searchPattern = $"{fn}*";

                // note: no need to delete thumbnails separately due to the aggressive searchPattern
                var dir = _tenantPathManager.GetPayloadDirectory(drive.Id, fileId);
                driveFileReaderWriter.DeleteFilesInDirectory(dir, searchPattern);
            });
        }

        public long GetPayloadDiskUsage(StorageDrive drive, Guid fileId)
        {
            var result = Benchmark.Milliseconds(logger, "GetPayloadDiskUsage", () =>
            {
                var payloadFilePath = _tenantPathManager.GetPayloadDirectory(drive.Id, fileId);
                if (!driveFileReaderWriter.DirectoryExists(payloadFilePath))
                {
                    return 0;
                }

                var usage = 0L;
                var filePaths = driveFileReaderWriter.GetFilesInDirectory(payloadFilePath!);
                foreach (var filePath in filePaths)
                {
                    var info = new FileInfo(filePath);
                    usage += info.Length;
                }

                return usage;
            });
            return result;
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
            var path = _tenantPathManager.GetThumbnailDirectoryAndFileName(drive.Id, fileId, descriptor.Key,descriptor.Uid,
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
        /// Removes all traces of a file and deletes its record from the index
        /// </summary>
        public async Task HardDeleteAsync(StorageDrive drive, Guid fileId)
        {
            Benchmark.Milliseconds(logger, "HardDeleteAsync", () => { HardDeleteAllPayloadFiles(drive, fileId); });
            await driveQuery.HardDeleteFileHeaderAsync(drive, new InternalDriveFileId(drive, fileId));
        }

        /// <summary>
        /// Moves the specified <param name="sourceFile"></param> to long term storage.
        /// Returns the storage UID used in the filename
        /// </summary>
        public void CopyPayloadToLongTerm(StorageDrive drive, Guid targetFileId, PayloadDescriptor descriptor, string sourceFile)
        {
            Benchmark.Milliseconds(logger, nameof(CopyPayloadToLongTerm), () =>
            {
                //if (!File.Exists(sourceFile))
                //{
                //    throw new OdinSystemException($"Payload: source file does not exist: {sourceFile}");
                //}

                var destinationFile = _tenantPathManager.GetPayloadDirectoryAndFileName(drive.Id, targetFileId, descriptor.Key, descriptor.Uid, ensureExists: true);
                driveFileReaderWriter.CopyFileSafely(sourceFile, destinationFile);
                logger.LogDebug("Payload: copied {sourceFile} to {destinationFile}", sourceFile, destinationFile);
            });
        }

        public void CopyThumbnailToLongTerm(StorageDrive drive, Guid targetFileId, string sourceThumbnailFilePath,
            PayloadDescriptor payloadDescriptor,
            ThumbnailDescriptor thumbnailDescriptor)
        {
            Benchmark.Milliseconds(logger, "MoveThumbnailToLongTerm", () =>
            {
                //if (!File.Exists(sourceThumbnailFilePath))
                //{
                //    throw new OdinSystemException($"Thumbnail: source file does not exist: {sourceThumbnailFilePath}");
                //}

                var payloadKey = payloadDescriptor.Key;

                TenantPathManager.AssertValidPayloadKey(payloadKey);
                var destinationFile = _tenantPathManager.GetThumbnailDirectoryAndFileName(drive.Id, targetFileId, payloadKey, payloadDescriptor.Uid,
                    thumbnailDescriptor.PixelWidth, thumbnailDescriptor.PixelHeight);

                var dir = Path.GetDirectoryName(destinationFile) ??
                          throw new OdinSystemException("Destination folder was null");
                logger.LogInformation("Creating Directory for thumbnail: {dir}", dir);
                driveFileReaderWriter.CreateDirectory(dir);

                driveFileReaderWriter.CopyFileSafely(sourceThumbnailFilePath, destinationFile);
                logger.LogDebug("Thumbnail: moved {sourceThumbnailFilePath} to {destinationFile}",
                    sourceThumbnailFilePath, destinationFile);
            });
        }

        public async Task<ServerFileHeader> GetServerFileHeader(StorageDrive drive, Guid fileId, FileSystemType fileSystemType)
        {
            var header = await driveQuery.GetFileHeaderAsync(drive, fileId, fileSystemType);
            return header;
        }

        /// <summary>
        /// Removes any payloads that are not in the provided list
        /// </summary>
        public void HardDeleteDeadPayloadFiles(StorageDrive drive, Guid fileId, List<PayloadDescriptor> deadPayloads)
        {
            if (drive.TargetDriveInfo == SystemDriveConstants.FeedDrive)
            {
                logger.LogDebug("HardDeleteOrphanPayloadFiles called on feed drive; ignoring since feed does not receive the payloads");
                return;
            }

            Benchmark.Milliseconds(logger, nameof(HardDeleteDeadPayloadFiles), () =>
            {
                var zombiePayloadFiles = GetZombiePayloadFilePaths(drive, fileId, deadPayloads);
                foreach (var zombiePayload in zombiePayloadFiles)
                {
                    //Note: this also kills the thumbnails for this file
                    HardDeletePayloadFile(drive, fileId, zombiePayload.Key, zombiePayload.Uid);
                }
            });
        }

        private List<ParsedPayloadFileRecord> GetZombiePayloadFilePaths(StorageDrive drive, Guid fileId, List<PayloadDescriptor> deadPayloads)
        {
            /*
              ├── 1fedce18c0022900efbb396f9796d3d0-prfl_pic-113599297775861760.payload
              ├── 1fedce18c0022900efbb396f9796d3d0-prfl_pic-113599297775861760-20x20.thumb
              ├── 1fedce18c0022900efbb396f9796d3d0-prfl_pic-113599297775861760-400x400.thumb
              ├── 1fedce18c0022900efbb396f9796d3d0-prfl_pic-113599297775861760-500x500.thumb
            */

            var payloadFileDirectory = _tenantPathManager.GetPayloadDirectory(drive.Id, fileId);
            if (!driveFileReaderWriter.DirectoryExists(payloadFileDirectory))
            {
                return [];
            }

            var searchPattern = GetPayloadSearchMask(fileId);
            var files = driveFileReaderWriter.GetFilesInDirectory(payloadFileDirectory, searchPattern);

            var zombies = new List<ParsedPayloadFileRecord>();
            foreach (var payloadFilePath in files)
            {
                var filename = Path.GetFileNameWithoutExtension(payloadFilePath);
                var fileRecord = TenantPathManager.ParsePayloadFilename(filename);

                bool isZombie = deadPayloads.Any(p => p.Key.Equals(fileRecord.Key, StringComparison.InvariantCultureIgnoreCase) &&
                                                      p.Uid.uniqueTime == fileRecord.Uid.uniqueTime);
                // 🧟🧟🧟
                if (isZombie)
                {
                    zombies.Add(fileRecord);
                }
            }

            return zombies;
        }

        private List<ParsedPayloadFileRecord> GetOrphanedPayloads(string[] files, List<PayloadDescriptor> expectedPayloads)
        {
            // examine all payload files for a given fileId, regardless of key.
            // we'll compare the file below before deleting

            var orphanFiles = new List<ParsedPayloadFileRecord>();

            foreach (var payloadFilePath in files)
            {
                var filename = Path.GetFileNameWithoutExtension(payloadFilePath);
                var fileRecord = TenantPathManager.ParsePayloadFilename(filename);

                bool isKept = expectedPayloads.Any(p => p.Key.Equals(fileRecord.Key, StringComparison.InvariantCultureIgnoreCase) &&
                                                        p.Uid.uniqueTime == fileRecord.Uid.uniqueTime);

                if (!isKept)
                {
                    orphanFiles.Add(fileRecord);
                }
            }

            return orphanFiles;
        }

        private List<ParsedThumbnailFileRecord> GetOrphanThumbnails(StorageDrive drive, Guid fileId, PayloadDescriptor payloadDescriptor)
        {
            // examine all payload files for a given fileId, regardless of key.
            // we'll compare the file below before deleting

            var expectedThumbnails = payloadDescriptor.Thumbnails?.ToList() ?? [];
            var dir = _tenantPathManager.GetPayloadDirectory(drive.Id, fileId);
            if (driveFileReaderWriter.DirectoryExists(dir))
            {
                return [];
            }

            // ├── 1fedce18c0022900efbb396f9796d3d0-prfl_pic-113599297775861760-*x*.thumb
            var thumbnailSearchPatternForPayload = GetThumbnailSearchMask(fileId, payloadDescriptor.Key, payloadDescriptor.Uid);
            var thumbnailFilePathsForPayload = driveFileReaderWriter.GetFilesInDirectory(dir, thumbnailSearchPatternForPayload);
            logger.LogDebug("Deleting thumbnails: Found {count} for file({fileId}) with path-pattern ({pattern})",
                thumbnailFilePathsForPayload.Length,
                fileId,
                thumbnailSearchPatternForPayload);

            var orphans = new List<ParsedThumbnailFileRecord>();

            foreach (var thumbnailFilePath in thumbnailFilePathsForPayload)
            {
                var filename = Path.GetFileNameWithoutExtension(thumbnailFilePath);
                var thumbnailFileRecord = TenantPathManager.ParseThumbnailFilename(filename);

                // is the file from the payload and thumbnail size
                var keepThumbnail = payloadDescriptor.Key.Equals(thumbnailFileRecord.Key, StringComparison.InvariantCultureIgnoreCase) &&
                                    payloadDescriptor.Uid.uniqueTime == thumbnailFileRecord.Uid.uniqueTime &&
                                    expectedThumbnails.Exists(thumb => thumb.PixelWidth == thumbnailFileRecord.Width &&
                                                                       thumb.PixelHeight == thumbnailFileRecord.Height);
                if (!keepThumbnail)
                {
                    orphans.Add(thumbnailFileRecord);
                }
            }

            return orphans;
        }

        public async Task<bool> HasOrphanPayloadsOrThumbnails(InternalDriveFileId file, List<PayloadDescriptor> expectedPayloads)
        {
            var drive = await driveManager.GetDriveAsync(file.DriveId);
            var payloadFileDirectory = _tenantPathManager.GetPayloadDirectory(drive.Id, file.FileId);

            var searchPattern = GetPayloadSearchMask(file.FileId);
            var files = driveFileReaderWriter.GetFilesInDirectory(payloadFileDirectory, searchPattern);
            var orphans = GetOrphanedPayloads(files, expectedPayloads);

            if (orphans.Any())
            {
                return true;
            }

            foreach (var descriptor in expectedPayloads)
            {
                var thumbnailOrphans = GetOrphanThumbnails(drive, file.FileId, descriptor);
                if (thumbnailOrphans.Any())
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Removes all thumbnails on disk which are not in the provided list.
        /// </summary>
        private void HardDeleteOrphanThumbnailFiles(StorageDrive drive, Guid fileId, PayloadDescriptor payloadDescriptor)
        {
            Benchmark.Milliseconds(logger, nameof(HardDeleteOrphanThumbnailFiles), () =>
            {
                var orphanedThumbnailFileRecords = GetOrphanThumbnails(drive, fileId, payloadDescriptor);
                foreach (var orphanThumbnail in orphanedThumbnailFileRecords)
                {
                    HardDeleteThumbnailFile(drive, fileId, payloadDescriptor.Key, payloadDescriptor.Uid,
                        orphanThumbnail.Width, orphanThumbnail.Height);
                }
            });
        }

        public async Task DeleteUnassociatedTargetFiles(InternalDriveFileId targetFile)
        {
            try
            {
                var drive = await driveManager.GetDriveAsync(targetFile.DriveId);
                HardDeleteDeadPayloadFiles(drive, targetFile.FileId, []);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed deleting unassociated target files {file}", targetFile);
            }
        }

        private string GetThumbnailSearchMask(Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid)
        {
            var extension = TenantPathManager.CreateThumbnailFileExtensionStarStar(payloadKey, payloadUid);
            return $"{TenantPathManager.GuidToPathSafeString(fileId)}{TenantPathManager.FileNameSectionDelimiter}{extension}";
        }

        private string GetPayloadSearchMask(Guid fileId)
        {
            var extension = DriveFileUtility.GetPayloadFileExtensionStarStar();
            var mask = $"{TenantPathManager.GuidToPathSafeString(fileId)}{TenantPathManager.FileNameSectionDelimiter}{extension}";
            return mask;
        }
    }
}