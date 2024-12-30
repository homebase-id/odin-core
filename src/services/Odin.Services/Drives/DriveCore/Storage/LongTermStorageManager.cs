using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Util;

namespace Odin.Services.Drives.DriveCore.Storage
{
    public class LongTermStorageManager
    {
        private readonly ILogger<LongTermStorageManager> _logger;

        private readonly DriveFileReaderWriter _driveFileReaderWriter;
        private readonly DriveManager _driveManager;
        private readonly DriveQuery _driveQuery;

        private const string ThumbnailDelimiter = "_";
        private const string ThumbnailSizeDelimiter = "x";
        private static readonly string ThumbnailSuffixFormatSpecifier = $"{ThumbnailDelimiter}{{0}}{ThumbnailSizeDelimiter}{{1}}";

        public LongTermStorageManager(
            ILogger<LongTermStorageManager> logger,
            DriveFileReaderWriter driveFileReaderWriter,
            DriveManager driveManager,
            DriveQuery driveQuery)
        {
            _logger = logger;
            _driveFileReaderWriter = driveFileReaderWriter;
            _driveManager = driveManager;
            _driveQuery = driveQuery;
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

        public async Task SoftDeleteFileHeader(ServerFileHeader header)
        {
            OdinValidationUtils.AssertNotNull(header, nameof(header));
            await _driveQuery.SoftDeleteFileHeader(header);
        }

        public async Task SaveTransferHistory(StorageDrive drive, Guid fileId, RecipientTransferHistory history)
        {
            OdinValidationUtils.AssertNotNull(history, nameof(history));
            await _driveQuery.SaveTransferHistoryAsync(drive, fileId, history);
        }

        public async Task DeleteTransferHistory(StorageDrive drive, Guid fileId)
        {
            await _driveQuery.SaveTransferHistoryAsync(drive, fileId, null);
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

        public async Task DeleteThumbnailFile(StorageDrive drive, Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid, int height, int width)
        {
            string fileName = GetThumbnailFileName(fileId, width, height, payloadKey, payloadUid);
            string dir = GetFilePath(drive, fileId, FilePart.Thumb);
            string path = Path.Combine(dir, fileName);

            await _driveFileReaderWriter.DeleteFileAsync(path);
        }

        public async Task DeletePayloadFile(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor)
        {
            string path = GetPayloadFilePath(drive, fileId, descriptor);
            await _driveFileReaderWriter.DeleteFileAsync(path);
        }

        public async Task DeleteAllPayloadFiles(StorageDrive drive, Guid fileId)
        {
            var searchPattern = this.GetFilename(fileId, "-*", FilePart.Payload);
            string dir = GetFilePath(drive, fileId, FilePart.Payload);
            await _driveFileReaderWriter.DeleteFilesInDirectoryAsync(dir, searchPattern);
        }

        public async Task<Int64> GetPayloadDiskUsage(StorageDrive drive, Guid fileId)
        {
            string payloadFilePath = GetPayloadPath(drive, fileId);
            if (!await _driveFileReaderWriter.DirectoryExists(payloadFilePath))
            {
                return 0;
            }

            Int64 usage = 0;
            var filePaths = _driveFileReaderWriter.GetFilesInDirectory(payloadFilePath!);
            foreach (var filePath in filePaths)
            {
                var info = new FileInfo(filePath);
                usage += info.Length;
            }

            return usage;
        }

        public async Task<Stream> GetPayloadStream(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor, FileChunk chunk = null)
        {
            var path = GetPayloadFilePath(drive, fileId, descriptor);
            _logger.LogDebug("Get Chunked Stream called on file [{path}]", path);

            Stream fileStream;
            try
            {
                fileStream = await _driveFileReaderWriter.OpenStreamForReading(path);
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
                        throw new OdinClientException("Chunk start position is greater than length", OdinClientErrorCode.InvalidChunkStart);
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


        /// <summary>
        /// Gets a read stream of the thumbnail
        /// </summary>
        public async Task<Stream> GetThumbnailStream(StorageDrive drive, Guid fileId, int width, int height, string payloadKey, UnixTimeUtcUnique payloadUid)
        {
            string fileName = GetThumbnailFileName(fileId, width, height, payloadKey, payloadUid);
            string dir = GetFilePath(drive, fileId, FilePart.Thumb);
            string path = Path.Combine(dir, fileName);

            try
            {
                var fileStream = await _driveFileReaderWriter.OpenStreamForReading(path);
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
        }

        /// <summary>
        /// Checks if the header file exists on disk.  Does not check the validity of the header
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
        public async Task HardDelete(StorageDrive drive, Guid fileId)
        {
            await DeleteAllThumbnails(drive, fileId);
            await DeleteAllPayloadFiles(drive, fileId);
            await _driveQuery.HardDeleteFileHeaderAsync(drive, GetInternalFile(drive, fileId));
        }

        /// <summary>
        /// Removes the contents of the meta file while permanently deletes the payload and thumbnails.  Retains some fields of the metafile and updates the index accordingly
        /// </summary>
        /// <param name="fileId"></param>
        public async Task DeleteAttachments(StorageDrive drive, Guid fileId)
        {
            await DeleteAllThumbnails(drive, fileId);
            await DeleteAllPayloadFiles(drive, fileId);
        }

        /// <summary>
        /// Moves the specified <param name="sourceFile"></param> to long term storage.  Returns the storage UID used in the filename
        /// </summary>
        public async Task MovePayloadToLongTerm(StorageDrive drive, Guid targetFileId, PayloadDescriptor descriptor, string sourceFile)
        {
            var destinationFile = GetPayloadFilePath(drive, targetFileId, descriptor, ensureExists: true);
            await _driveFileReaderWriter.MoveFile(sourceFile, destinationFile);
        }

        public async Task MoveThumbnailToLongTermAsync(StorageDrive drive, Guid targetFileId, string sourceThumbnailFilePath, PayloadDescriptor payloadDescriptor,
            ThumbnailDescriptor thumbnailDescriptor)
        {
            var payloadKey = payloadDescriptor.Key;

            DriveFileUtility.AssertValidPayloadKey(payloadKey);
            var destinationFile = GetThumbnailPath(drive, targetFileId, thumbnailDescriptor.PixelWidth, thumbnailDescriptor.PixelHeight, payloadKey,
                payloadDescriptor.Uid);

            string dir = Path.GetDirectoryName(destinationFile) ?? throw new OdinSystemException("Destination folder was null");
            _logger.LogInformation("Creating Directory for thumbnail: {dir}", dir);
            _driveFileReaderWriter.CreateDirectory(dir);

            await _driveFileReaderWriter.MoveFile(sourceThumbnailFilePath, destinationFile);
            _logger.LogDebug("File Moved to {destinationFile}", destinationFile);
        }

        public async Task<ServerFileHeader> GetServerFileHeader(StorageDrive drive, Guid fileId, FileSystemType fileSystemType)
        {
            var header = await _driveQuery.GetFileHeaderAsync(drive, fileId, fileSystemType);
            return header;
        }

        /// <summary>
        /// Removes any payloads that are not in the provided list
        /// </summary>
        public async Task DeleteMissingPayloadsAsync(StorageDrive drive, Guid fileId, List<PayloadDescriptor> payloadsToKeep)
        {
            //get all payloads in the path
            var payloadFileDirectory = GetPayloadPath(drive, fileId);

            if (await _driveFileReaderWriter.DirectoryExists(payloadFileDirectory))
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
                        await _driveFileReaderWriter.DeleteFileAsync(payloadFilePath);
                    }
                }
            }
        }

        /// <summary>
        /// Removes all thumbnails on disk which are not in the provided list.
        /// </summary>
        public async Task DeleteMissingThumbnailFilesAsync(StorageDrive drive, Guid fileId, IEnumerable<ThumbnailDescriptor> thumbnailsToKeep)
        {
            var list = thumbnailsToKeep?.ToList() ?? [];

            var dir = GetFilePath(drive, fileId, FilePart.Thumb);

            if (await _driveFileReaderWriter.DirectoryExists(dir))
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
                        await _driveFileReaderWriter.DeleteFileAsync(thumbnailFilePath);
                    }
                }
            }
        }

        private string GetThumbnailFileName(Guid fileId, int width, int height, string payloadKey, UnixTimeUtcUnique payloadUid)
        {
            var extension = DriveFileUtility.GetThumbnailFileExtension(payloadKey, payloadUid, width, height);
            return $"{DriveFileUtility.GetFileIdForStorage(fileId)}{DriveFileUtility.FileNameSectionDelimiter}{extension}";
        }

        private string GetThumbnailPath(StorageDrive drive, Guid fileId, int width, int height, string payloadKey, UnixTimeUtcUnique payloadUid)
        {
            var thumbnailFileName = GetThumbnailFileName(fileId, width, height, payloadKey, payloadUid);
            var filePath = GetFilePath(drive, fileId, FilePart.Thumb);
            var thumbnailPath = Path.Combine(filePath, thumbnailFileName);
            return thumbnailPath;
        }

        private string GetFilePath(StorageDrive drive, Guid fileId, FilePart filePart, bool ensureExists = false)
        {
            var path = filePart is FilePart.Payload or FilePart.Thumb ? drive.GetLongTermPayloadStoragePath() : throw new OdinSystemException($"Invalid FilePart {filePart}");

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
                _driveFileReaderWriter.CreateDirectory(dir);
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

        private async Task DeleteAllThumbnails(StorageDrive drive, Guid fileId)
        {
            var thumbnailSearchPattern = string.Format(ThumbnailSuffixFormatSpecifier, "*", "*");
            var searchPattern = this.GetFilename(fileId, thumbnailSearchPattern, FilePart.Thumb);
            string dir = GetFilePath(drive, fileId, FilePart.Thumb);
            await _driveFileReaderWriter.DeleteFilesInDirectoryAsync(dir, searchPattern);
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