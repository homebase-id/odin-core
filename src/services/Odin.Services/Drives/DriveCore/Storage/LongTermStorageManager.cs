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
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Util;

namespace Odin.Services.Drives.DriveCore.Storage
{
    public class LongTermStorageManager
    {
        private readonly ILogger<LongTermStorageManager> _logger;

        private readonly DriveFileReaderWriter _driveFileReaderWriter;
        private readonly DriveQuery _driveQuery;
        private readonly ScopedIdentityTransactionFactory _scopedIdentityTransactionFactory;
        private readonly TableDriveTransferHistory _tableDriveTransferHistory;
        private readonly TableDriveMainIndex _driveMainIndex;

        private const string ThumbnailDelimiter = "_";
        private const string ThumbnailSizeDelimiter = "x";
        private static readonly string ThumbnailSuffixFormatSpecifier = $"{ThumbnailDelimiter}{{0}}{ThumbnailSizeDelimiter}{{1}}";

        public LongTermStorageManager(
            ILogger<LongTermStorageManager> logger,
            DriveFileReaderWriter driveFileReaderWriter,
            DriveQuery driveQuery,
            ScopedIdentityTransactionFactory scopedIdentityTransactionFactory,
            TableDriveTransferHistory tableDriveTransferHistory,
            TableDriveMainIndex driveMainIndex)
        {
            _logger = logger;
            _driveFileReaderWriter = driveFileReaderWriter;
            _driveQuery = driveQuery;
            _scopedIdentityTransactionFactory = scopedIdentityTransactionFactory;
            _tableDriveTransferHistory = tableDriveTransferHistory;
            _driveMainIndex = driveMainIndex;
        }

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
        public async Task SaveFileHeader(StorageDrive drive, ServerFileHeader header)
        {
            OdinValidationUtils.AssertNotNull(header, nameof(header));
            await _driveQuery.SaveFileHeaderAsync(drive, header);
        }

        public async Task SaveLocalMetadataAsync(InternalDriveFileId file, LocalAppMetadata metadata)
        {
            OdinValidationUtils.AssertIsTrue(file.IsValid(), "file is invalid");

            var json = OdinSystemSerializer.Serialize(metadata);
            await _driveQuery.SaveLocalMetadataAsync(file.DriveId, file.FileId, metadata.VersionTag, json);
        }

        public async Task SaveLocalMetadataTagsAsync(InternalDriveFileId file, LocalAppMetadata metadata)
        {
            OdinValidationUtils.AssertIsTrue(file.IsValid(), "file is invalid");
            await _driveQuery.SaveLocalMetadataTagsAsync(file.DriveId, file.FileId, metadata);
        }

        public async Task SoftDeleteFileHeader(ServerFileHeader header)
        {
            OdinValidationUtils.AssertNotNull(header, nameof(header));
            await _driveQuery.SoftDeleteFileHeader(header);
        }

        public async Task<(RecipientTransferHistory updatedHistory, UnixTimeUtc modifiedTime)> InitiateTransferHistoryAsync(
            Guid driveId,
            Guid fileId,
            OdinId recipient)
        {
            _logger.LogDebug("InitiateTransferHistoryAsync for file: {f} on drive: {d}", fileId, driveId);

            await using var tx = await _scopedIdentityTransactionFactory.BeginStackedTransactionAsync();
            var added = await _tableDriveTransferHistory.TryAddInitialRecordAsync(driveId, fileId, recipient);
            if (!added)
            {
                _logger.LogDebug("InitiateTransferHistoryAsync: Insert failed, now updating for file: {f} on drive: {d}", fileId, driveId);

                var affectedRows = await _tableDriveTransferHistory.UpdateTransferHistoryRecordAsync(driveId, fileId, recipient,
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

            _logger.LogDebug("Begin Transaction for SaveTransferHistoryAsync file: {f}, driveId {d}. UpdateData: {u}", fileId, driveId,
                updateData.ToDebug());

            await using var tx = await _scopedIdentityTransactionFactory.BeginStackedTransactionAsync();

            await _tableDriveTransferHistory.UpdateTransferHistoryRecordAsync(driveId, fileId, recipient,
                (int?)updateData.LatestTransferStatus,
                updateData.VersionTag,
                updateData.IsInOutbox,
                updateData.IsReadByRecipient);

            var (history, modified) = await UpdateTransferHistorySummary(driveId, fileId);

            tx.Commit();

            _logger.LogDebug("End Transaction for SaveTransferHistoryAsync file: {f}, driveId {d}", fileId, driveId);

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

            var (count, modified) = await _driveMainIndex.UpdateTransferSummaryAsync(driveId, fileId, json);

            // TODO: What if count is zero?

            return (history, new UnixTimeUtc(modified));
        }

        public async Task DeleteTransferHistoryAsync(StorageDrive drive, Guid fileId)
        {
            await _tableDriveTransferHistory.DeleteAllRowsAsync(drive.Id, fileId);
        }

        public async Task SaveReactionHistory(StorageDrive drive, Guid fileId, ReactionSummary summary)
        {
            OdinValidationUtils.AssertNotNull(summary, nameof(summary));
            await _driveQuery.SaveReactionSummary(drive, fileId, summary);
        }

        public async Task DeleteReactionSummary(StorageDrive drive, Guid fileId)
        {
            await _driveQuery.SaveReactionSummary(drive, fileId, null);
        }

        public void DeleteThumbnailFile(StorageDrive drive, Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid, int height,
            int width)
        {
            Benchmark.Milliseconds(_logger, "DeleteThumbnailFile", () =>
            {
                var fileName = GetThumbnailFileName(fileId, width, height, payloadKey, payloadUid);
                var dir = GetFilePath(drive, fileId, FilePart.Thumb);
                var path = Path.Combine(dir, fileName);

                _driveFileReaderWriter.DeleteFile(path);
            });
        }

        public void DeletePayloadFile(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor)
        {
            Benchmark.Milliseconds(_logger, "DeletePayloadFile", () =>
            {
                var path = GetPayloadFilePath(drive, fileId, descriptor);
                _driveFileReaderWriter.DeleteFile(path);
            });
        }

        public void DeleteAllPayloadFiles(StorageDrive drive, Guid fileId)
        {
            Benchmark.Milliseconds(_logger, "DeleteAllPayloadFiles", () =>
            {
                var searchPattern = this.GetFilename(fileId, "-*", FilePart.Payload);
                var dir = GetFilePath(drive, fileId, FilePart.Payload);
                _driveFileReaderWriter.DeleteFilesInDirectory(dir, searchPattern);
            });
        }

        public long GetPayloadDiskUsage(StorageDrive drive, Guid fileId)
        {
            var result = Benchmark.Milliseconds(_logger, "GetPayloadDiskUsage", () =>
            {
                var payloadFilePath = GetPayloadPath(drive, fileId);
                if (!_driveFileReaderWriter.DirectoryExists(payloadFilePath))
                {
                    return 0;
                }

                var usage = 0L;
                var filePaths = _driveFileReaderWriter.GetFilesInDirectory(payloadFilePath!);
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
            var path = GetPayloadFilePath(drive, fileId, descriptor);
            return _driveFileReaderWriter.FileExists(path);
        }

        public bool ThumbnailExistsOnDisk(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor, 
            ThumbnailDescriptor thumbnailDescriptor)
        {
            var path = GetThumbnailPath(drive, fileId, thumbnailDescriptor.PixelWidth,
                thumbnailDescriptor.PixelHeight,
                descriptor.Key,
                descriptor.Uid);

            return _driveFileReaderWriter.FileExists(path);
        }

        public async Task<Stream> GetPayloadStream(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor, FileChunk chunk = null)
        {
            var result = await Benchmark.MillisecondsAsync(_logger, "GetPayloadStream", async () => await Execute());
            return result;

            async Task<Stream> Execute()
            {
                var path = GetPayloadFilePath(drive, fileId, descriptor);
                _logger.LogDebug("Get Chunked Stream called on file [{path}]", path);

                Stream fileStream;
                try
                {
                    fileStream = _driveFileReaderWriter.OpenStreamForReading(path);
                    _logger.LogDebug("File size: {size} bytes", fileStream.Length);
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
            var result = Benchmark.Milliseconds(_logger, "GetThumbnailStream", () =>
            {
                var fileName = GetThumbnailFileName(fileId, width, height, payloadKey, payloadUid);
                var dir = GetFilePath(drive, fileId, FilePart.Thumb);
                var path = Path.Combine(dir, fileName);

                try
                {
                    var fileStream = _driveFileReaderWriter.OpenStreamForReading(path);
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
            var list = await _tableDriveTransferHistory.GetAsync(driveId, fileId);
            return list.Select(item =>
                new RecipientTransferHistoryItem
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
            Benchmark.Milliseconds(_logger, "HardDeleteAsync", () =>
            {
                DeleteAllThumbnails(drive, fileId);
                DeleteAllPayloadFiles(drive, fileId);
            });
            await _driveQuery.HardDeleteFileHeaderAsync(drive, GetInternalFile(drive, fileId));
        }

        /// <summary>
        /// Removes the contents of the meta file while permanently deletes the payload and thumbnails.  Retains some fields of the metafile and updates the index accordingly
        /// </summary>
        public void DeleteAttachments(StorageDrive drive, Guid fileId)
        {
            Benchmark.Milliseconds(_logger, "DeleteAttachments", () =>
            {
                DeleteAllThumbnails(drive, fileId);
                DeleteAllPayloadFiles(drive, fileId);
            });
        }

        /// <summary>
        /// Moves the specified <param name="sourceFile"></param> to long term storage.  Returns the storage UID used in the filename
        /// </summary>
        public void MovePayloadToLongTerm(StorageDrive drive, Guid targetFileId, PayloadDescriptor descriptor, string sourceFile)
        {
            Benchmark.Milliseconds(_logger, "MovePayloadToLongTerm", () =>
            {
                if (!File.Exists(sourceFile))
                {
                    throw new OdinSystemException($"Payload: source file does not exist: {sourceFile}");
                }

                var destinationFile = GetPayloadFilePath(drive, targetFileId, descriptor, ensureExists: true);
                _driveFileReaderWriter.MoveFile(sourceFile, destinationFile);
                _logger.LogDebug("Payload: moved {sourceFile} to {destinationFile}", sourceFile, destinationFile);
            });
        }

        public void MoveThumbnailToLongTerm(StorageDrive drive, Guid targetFileId, string sourceThumbnailFilePath,
            PayloadDescriptor payloadDescriptor,
            ThumbnailDescriptor thumbnailDescriptor)
        {
            Benchmark.Milliseconds(_logger, "MoveThumbnailToLongTerm", () =>
            {
                if (!File.Exists(sourceThumbnailFilePath))
                {
                    throw new OdinSystemException($"Thumbnail: source file does not exist: {sourceThumbnailFilePath}");
                }

                var payloadKey = payloadDescriptor.Key;

                DriveFileUtility.AssertValidPayloadKey(payloadKey);
                var destinationFile = GetThumbnailPath(drive, targetFileId, thumbnailDescriptor.PixelWidth,
                    thumbnailDescriptor.PixelHeight,
                    payloadKey,
                    payloadDescriptor.Uid);

                var dir = Path.GetDirectoryName(destinationFile) ??
                          throw new OdinSystemException("Destination folder was null");
                _logger.LogInformation("Creating Directory for thumbnail: {dir}", dir);
                _driveFileReaderWriter.CreateDirectory(dir);

                _driveFileReaderWriter.MoveFile(sourceThumbnailFilePath, destinationFile);
                _logger.LogDebug("Thumbnail: moved {sourceThumbnailFilePath} to {destinationFile}",
                    sourceThumbnailFilePath, destinationFile);
            });
        }

        public async Task<ServerFileHeader> GetServerFileHeader(StorageDrive drive, Guid fileId, FileSystemType fileSystemType)
        {
            var header = await _driveQuery.GetFileHeaderAsync(drive, fileId, fileSystemType);
            return header;
        }

        /// <summary>
        /// Removes any payloads that are not in the provided list
        /// </summary>
        public void DeleteMissingPayloads(StorageDrive drive, Guid fileId, List<PayloadDescriptor> payloadsToKeep)
        {
            Benchmark.Milliseconds(_logger, "DeleteMissingPayloads", () =>
            {
                //get all payloads in the path
                var payloadFileDirectory = GetPayloadPath(drive, fileId);

                if (_driveFileReaderWriter.DirectoryExists(payloadFileDirectory))
                {
                    var searchPattern = string.Format(DriveFileUtility.PayloadExtensionSpecifier, "*");

                    var files = _driveFileReaderWriter.GetFilesInDirectory(payloadFileDirectory, searchPattern);
                    foreach (var payloadFilePath in files)
                    {
                        // get the payload key from the filepath
                        // Given a payload key of "test001
                        // Filename w/o extension = "c1c63e18-40a2-9700-7b6a-2f1d51ee3972-test001"
                        var filename = Path.GetFileNameWithoutExtension(payloadFilePath);
                        var payloadKeyOnDisk = filename.Split(DriveFileUtility.PayloadDelimiter)[1];

                        var keepPayload = payloadsToKeep.Exists(p => p.Key == payloadKeyOnDisk);
                        if (!keepPayload)
                        {
                            _driveFileReaderWriter.DeleteFile(payloadFilePath);
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Removes all thumbnails on disk which are not in the provided list.
        /// </summary>
        public void DeleteMissingThumbnailFiles(StorageDrive drive, Guid fileId,
            IEnumerable<ThumbnailDescriptor> thumbnailsToKeep)
        {
            Benchmark.Milliseconds(_logger, "DeleteMissingThumbnailFiles", () =>
            {
                var list = thumbnailsToKeep?.ToList() ?? [];

                var dir = GetFilePath(drive, fileId, FilePart.Thumb);

                if (_driveFileReaderWriter.DirectoryExists(dir))
                {
                    var thumbnailSearchPattern = string.Format(ThumbnailSuffixFormatSpecifier, "*", "*");
                    var seekPath = this.GetFilename(fileId, thumbnailSearchPattern, FilePart.Thumb);

                    var files = _driveFileReaderWriter.GetFilesInDirectory(dir, seekPath);
                    foreach (var thumbnailFilePath in files)
                    {
                        // filename w/o extension = "c1c63e18-40a2-9700-7b6a-2f1d51ee3972-300x300"
                        var filename = Path.GetFileNameWithoutExtension(thumbnailFilePath);
                        var sizeParts = filename.Split(ThumbnailDelimiter)[1].Split(ThumbnailSizeDelimiter);
                        var width = int.Parse(sizeParts[0]);
                        var height = int.Parse(sizeParts[1]);

                        var keepThumbnail = list.Exists(thumb => thumb.PixelWidth == width && thumb.PixelHeight == height);
                        if (!keepThumbnail)
                        {
                            _driveFileReaderWriter.DeleteFile(thumbnailFilePath);
                        }
                    }
                }
            });
        }

        private string GetThumbnailFileName(Guid fileId, int width, int height, string payloadKey, UnixTimeUtcUnique payloadUid)
        {
            var extension = DriveFileUtility.GetThumbnailFileExtension(payloadKey, payloadUid, width, height);
            return $"{DriveFileUtility.GetFileIdForStorage(fileId)}{DriveFileUtility.FileNameSectionDelimiter}{extension}";
        }

        private string GetThumbnailPath(StorageDrive drive, Guid fileId, int width, int height, string payloadKey,
            UnixTimeUtcUnique payloadUid)
        {
            var thumbnailFileName = GetThumbnailFileName(fileId, width, height, payloadKey, payloadUid);
            var filePath = GetFilePath(drive, fileId, FilePart.Thumb);
            var thumbnailPath = Path.Combine(filePath, thumbnailFileName);
            return thumbnailPath;
        }

        private string GetFilePath(StorageDrive drive, Guid fileId, FilePart filePart, bool ensureExists = false)
        {
            var path = filePart is FilePart.Payload or FilePart.Thumb
                ? drive.GetLongTermPayloadStoragePath()
                : throw new OdinSystemException($"Invalid FilePart {filePart}");

            //07e5070f-173b-473b-ff03-ffec2aa1b7b8
            //The positions in the time guid are hex values as follows
            //from new DateTimeOffset(2021, 7, 21, 23, 59, 59, TimeSpan.Zero);
            //07e5=year,07=month,0f=day,17=hour,3b=minute

            var parts = fileId.ToString().Split("-");
            var yearMonthDay = parts[0];
            var year = yearMonthDay.Substring(0, 4);
            var month = yearMonthDay.Substring(4, 2);
            var day = yearMonthDay.Substring(6, 2);
            var hourMinute = parts[1];
            var hour = hourMinute[..2];

            string dir = Path.Combine(path, year, month, day, hour);

            if (ensureExists)
            {
                Benchmark.Milliseconds(_logger, "GetFilePath/CreateDirectory", () =>
                    _driveFileReaderWriter.CreateDirectory(dir));
            }

            return dir;
        }

        private string GetFilename(Guid fileId, string suffix, FilePart part)
        {
            var fn = DriveFileUtility.GetFileIdForStorage(fileId);
            return $"{fn}{suffix}.{part.ToString().ToLower()}";
        }

        private string GetFilenameAndPath(StorageDrive drive, Guid fileId, FilePart part, bool ensureDirectoryExists = false)
        {
            var dir = GetFilePath(drive, fileId, part, ensureDirectoryExists);
            return Path.Combine(dir, GetFilename(fileId, string.Empty, part));
        }

        private string GetPayloadPath(StorageDrive drive, Guid fileId, bool ensureExists = false)
        {
            return GetFilePath(drive, fileId, FilePart.Payload, ensureExists);
        }

        private string GetPayloadFilePath(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor, bool ensureExists = false)
        {
            var extension = DriveFileUtility.GetPayloadFileExtension(descriptor.Key, descriptor.Uid);
            var payloadFileName = $"{DriveFileUtility.GetFileIdForStorage(fileId)}{DriveFileUtility.FileNameSectionDelimiter}{extension}";
            return Path.Combine(GetPayloadPath(drive, fileId, ensureExists), $"{payloadFileName}");
        }

        private void DeleteAllThumbnails(StorageDrive drive, Guid fileId)
        {
            Benchmark.Milliseconds(_logger, "DeleteAllThumbnails", () =>
            {
                var thumbnailSearchPattern = string.Format(ThumbnailSuffixFormatSpecifier, "*", "*");
                var searchPattern = this.GetFilename(fileId, thumbnailSearchPattern, FilePart.Thumb);
                var dir = GetFilePath(drive, fileId, FilePart.Thumb);
                _driveFileReaderWriter.DeleteFilesInDirectory(dir, searchPattern);
            });
        }

        private InternalDriveFileId GetInternalFile(StorageDrive drive, Guid fileId)
        {
            return new InternalDriveFileId()
            {
                FileId = fileId,
                DriveId = drive.Id
            };
        }
    }
}