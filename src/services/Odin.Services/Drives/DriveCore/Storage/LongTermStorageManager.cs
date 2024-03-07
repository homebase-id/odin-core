using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.DriveCore.Storage
{
    using System;

    public class LongTermStorageManager
    {
        private readonly ILogger<LongTermStorageManager> _logger;

        private readonly StorageDrive _drive;
        private readonly DriveFileReaderWriter _driveFileReaderWriter;

        private const string ThumbnailDelimiter = "_";
        private const string ThumbnailSizeDelimiter = "x";
        private static readonly string ThumbnailSuffixFormatSpecifier = $"{ThumbnailDelimiter}{{0}}{ThumbnailSizeDelimiter}{{1}}";

        public LongTermStorageManager(StorageDrive drive, ILogger<LongTermStorageManager> logger, DriveFileReaderWriter driveFileReaderWriter)
        {
            drive.EnsureDirectories();

            _logger = logger;
            _driveFileReaderWriter = driveFileReaderWriter;
            _drive = drive;
        }

        /// <summary>
        /// The drive managed by this instance
        /// </summary>
        public StorageDrive Drive => _drive;

        /// <summary>
        /// Creates an Id for storing a file
        /// </summary>
        /// <returns></returns>
        public Guid CreateFileId()
        {
            return SequentialGuid.CreateGuid();
        }

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        public Task WriteHeaderStream(Guid fileId, Stream stream)
        {
            string filePath = GetFilenameAndPath(fileId, FilePart.Header, true);
            var bytesWritten = _driveFileReaderWriter.WriteStream(filePath, stream);

            if (bytesWritten != stream.Length)
            {
                throw new OdinSystemException($"BytesWritten mismatch for file [{filePath}]");
            }

            return Task.CompletedTask;
        }

        public Task DeleteThumbnailFile(Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid, int height, int width)
        {
            string fileName = GetThumbnailFileName(fileId, width, height, payloadKey, payloadUid);
            string dir = GetFilePath(fileId, FilePart.Thumb);
            string path = Path.Combine(dir, fileName);

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return Task.CompletedTask;
        }

        public Task DeletePayloadFile(Guid fileId, PayloadDescriptor descriptor)
        {
            string path = GetPayloadFilePath(fileId, descriptor);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAllPayloadFiles(Guid fileId)
        {
            // string path = GetPayloadPath(fileId);
            var seekPath = this.GetFilename(fileId, "-*", FilePart.Payload);
            string dir = GetFilePath(fileId, FilePart.Payload);

            if (Directory.Exists(dir))
            {
                var payloads = Directory.GetFiles(dir, seekPath);
                foreach (var payload in payloads)
                {
                    File.Delete(payload);
                }
            }

            return Task.CompletedTask;
        }

        public Int64 GetPayloadDiskUsage(Guid fileId)
        {
            string payloadFilePath = GetPayloadPath(fileId);
            if (!Directory.Exists(payloadFilePath))
            {
                return 0;
            }

            Int64 usage = 0;
            var filePaths = Directory.GetFiles(payloadFilePath!);
            foreach (var filePath in filePaths)
            {
                var info = new FileInfo(filePath);
                usage += info.Length;
            }

            return usage;
        }

        public Task<Stream> GetPayloadStream(Guid fileId, PayloadDescriptor descriptor, FileChunk chunk = null)
        {
            var path = GetPayloadFilePath(fileId, descriptor);
            _logger.LogInformation($"Get Chunked Stream called on file [{path}]");

            Stream fileStream;
            try
            {
                fileStream = _driveFileReaderWriter.OpenStreamForReading(path);
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

                    return Task.FromResult((Stream)new MemoryStream(buffer, false));
                }
                finally
                {
                    fileStream.Dispose();
                }
            }

            return Task.FromResult(fileStream);
        }


        /// <summary>
        /// Gets a read stream of the thumbnail
        /// </summary>
        public Task<Stream> GetThumbnailStream(Guid fileId, int width, int height, string payloadKey, UnixTimeUtcUnique payloadUid)
        {
            string fileName = GetThumbnailFileName(fileId, width, height, payloadKey, payloadUid);
            string dir = GetFilePath(fileId, FilePart.Thumb);
            string path = Path.Combine(dir, fileName);

            try
            {
                var fileStream = _driveFileReaderWriter.OpenStreamForReading(path);
                return Task.FromResult(fileStream);
            }
            catch (IOException io)
            {
                if (io is FileNotFoundException || io is DirectoryNotFoundException)
                {
                    throw new OdinFileHeaderHasCorruptPayloadException($"Missing payload file [path:{path}] for key {payloadKey} with uid: {payloadUid.uniqueTime}");
                }

                throw;
            }
        }

        /// <summary>
        /// Ensures there is a valid file available for the given Id.
        /// </summary>
        /// <exception cref="InvalidDataException">Throw if the file for the given Id is invalid or does not exist</exception>
        public void AssertFileIsValid(Guid fileId)
        {
            if (fileId == Guid.Empty)
            {
                throw new OdinClientException("No file specified", OdinClientErrorCode.UnknownId);
            }

            if (!IsFileValid(fileId))
            {
                throw new OdinClientException("File does not contain all parts", OdinClientErrorCode.MissingUploadData);
            }
        }

        /// <summary>
        /// Checks if the file exists.  Returns true if all parts exist, otherwise false
        /// </summary>
        public bool FileExists(Guid fileId)
        {
            return IsFileValid(fileId);
        }

        private bool IsFileValid(Guid fileId)
        {
            string headerPath = GetFilenameAndPath(fileId, FilePart.Header);
            if (!File.Exists(headerPath))
            {
                return false;
            }

            var header = this.GetServerFileHeader(fileId).GetAwaiter().GetResult();
            if (header == null)
            {
                return false;
            }

            //TODO: this needs to be optimized by getting all files in the folder; then checking the filename exists
            foreach (var d in header.FileMetadata.Payloads)
            {
                var payloadFilePath = GetPayloadFilePath(fileId, d);
                if (!File.Exists(payloadFilePath))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Removes all traces of a file and deletes its record from the index
        /// </summary>
        public Task HardDelete(Guid fileId)
        {
            DeleteAllThumbnails(fileId);
            DeleteAllPayloadFiles(fileId);

            string metadata = GetFilenameAndPath(fileId, FilePart.Header);
            if (File.Exists(metadata))
            {
                File.Delete(metadata);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Removes the contents of the meta file while permanently deletes the payload and thumbnails.  Retains some fields of the metafile and updates the index accordingly
        /// </summary>
        /// <param name="fileId"></param>
        public Task DeleteAttachments(Guid fileId)
        {
            DeleteAllThumbnails(fileId);
            DeleteAllPayloadFiles(fileId);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Moves the specified <param name="sourceFile"></param> to long term storage.  Returns the storage UID used in the filename
        /// </summary>
        public Task MovePayloadToLongTerm(Guid targetFileId, PayloadDescriptor descriptor, string sourceFile)
        {
            var destinationFile = GetPayloadFilePath(targetFileId, descriptor, ensureExists: true);
            _driveFileReaderWriter.MoveFile(sourceFile, destinationFile);

            return Task.CompletedTask;
        }

        public Task MoveThumbnailToLongTerm(Guid targetFileId, string sourceThumbnailFilePath, PayloadDescriptor payloadDescriptor,
            ThumbnailDescriptor thumbnailDescriptor)
        {
            var payloadKey = payloadDescriptor.Key;

            DriveFileUtility.AssertValidPayloadKey(payloadKey);
            var destinationFile = GetThumbnailPath(targetFileId, thumbnailDescriptor.PixelWidth, thumbnailDescriptor.PixelHeight, payloadKey,
                payloadDescriptor.Uid);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile) ?? throw new OdinSystemException("Destination folder was null"));

            _driveFileReaderWriter.MoveFile(sourceThumbnailFilePath, destinationFile);
            _logger.LogInformation($"File Moved to {destinationFile}");

            return Task.CompletedTask;
        }

        /// <summary>
        /// Returns an enumeration of <see cref="FileMetadata"/>; ordered by the most recently modified
        /// </summary>
        /// <param name="pageOptions"></param>
        public async Task<IEnumerable<ServerFileHeader>> GetServerFileHeaders(PageOptions pageOptions)
        {
            // string path = this.Drive.GetStoragePath(StorageDisposition.LongTerm);
            string path = this.Drive.GetLongTermHeaderStoragePath();
            var options = new EnumerationOptions()
            {
                MatchCasing = MatchCasing.CaseInsensitive,
                RecurseSubdirectories = true,
                ReturnSpecialDirectories = false,
                IgnoreInaccessible = false,
                MatchType = MatchType.Win32
            };

            var results = new List<ServerFileHeader>();
            var filePaths = Directory.EnumerateFiles(path, $"*.{FilePart.Header.ToString().ToLower()}", options);
            foreach (string filePath in filePaths)
            {
                string filename = Path.GetFileNameWithoutExtension(filePath);
                Guid fileId = Guid.Parse(filename);
                var md = await this.GetServerFileHeader(fileId);
                results.Add(md);
            }

            return results;
        }

        public Task<ServerFileHeader> GetServerFileHeader(Guid fileId)
        {
            string headerFilepath = GetFilenameAndPath(fileId, FilePart.Header);
            if (!File.Exists(headerFilepath))
            {
                return Task.FromResult<ServerFileHeader>(null);
            }

            var bytes = _driveFileReaderWriter.GetAllFileBytes(headerFilepath);
            var header = OdinSystemSerializer.Deserialize<ServerFileHeader>(bytes.ToStringFromUtf8Bytes());
            return Task.FromResult(header);
        }

        /// <summary>
        /// Removes any payloads that are not in the provided list
        /// </summary>
        public Task DeleteMissingPayloads(Guid fileId, List<PayloadDescriptor> payloadsToKeep)
        {
            //get all payloads in the path
            var payloadFileDirectory = this.GetPayloadPath(fileId);

            if (Directory.Exists(payloadFileDirectory))
            {
                var searchPattern = string.Format(DriveFileUtility.PayloadExtensionSpecifier, "*");

                var files = Directory.GetFiles(payloadFileDirectory, searchPattern);
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
                        File.Delete(payloadFilePath);
                    }
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Removes all thumbnails on disk which are not in the provided list.
        /// </summary>
        public Task DeleteMissingThumbnailFiles(Guid fileId, IEnumerable<ThumbnailDescriptor> thumbnailsToKeep)
        {
            var list = thumbnailsToKeep?.ToList() ?? [];

            string dir = GetFilePath(fileId, FilePart.Thumb);

            if (Directory.Exists(dir))
            {
                var thumbnailSearchPattern = string.Format(ThumbnailSuffixFormatSpecifier, "*", "*");
                var seekPath = this.GetFilename(fileId, thumbnailSearchPattern, FilePart.Thumb);

                var files = Directory.GetFiles(dir, seekPath);
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
                        File.Delete(thumbnailFilePath);
                    }
                }
            }

            return Task.CompletedTask;
        }

        private string GetThumbnailFileName(Guid fileId, int width, int height, string payloadKey, UnixTimeUtcUnique payloadUid)
        {
            var extension = DriveFileUtility.GetThumbnailFileExtension(payloadKey, payloadUid, width, height);
            return $"{DriveFileUtility.GetFileIdForStorage(fileId)}{DriveFileUtility.FileNameSectionDelimiter}{extension}";
        }

        private string GetThumbnailPath(Guid fileId, int width, int height, string payloadKey, UnixTimeUtcUnique payloadUid)
        {
            var thumbnailFileName = GetThumbnailFileName(fileId, width, height, payloadKey, payloadUid);
            var filePath = GetFilePath(fileId, FilePart.Thumb);
            var thumbnailPath = Path.Combine(filePath, thumbnailFileName);
            return thumbnailPath;
        }

        private string GetFilePath(Guid fileId, FilePart filePart, bool ensureExists = false)
        {
            string path = filePart is FilePart.Payload or FilePart.Thumb ? _drive.GetLongTermPayloadStoragePath() : _drive.GetLongTermHeaderStoragePath();

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
                Directory.CreateDirectory(dir);
            }

            return dir;
        }

        private string GetFilename(Guid fileId, string suffix, FilePart part)
        {
            var fn = DriveFileUtility.GetFileIdForStorage(fileId);
            return $"{fn}{suffix}.{part.ToString().ToLower()}";
        }

        private string GetFilenameAndPath(Guid fileId, FilePart part, bool ensureDirectoryExists = false)
        {
            string dir = GetFilePath(fileId, part, ensureDirectoryExists);
            return Path.Combine(dir, GetFilename(fileId, string.Empty, part));
        }

        private string GetPayloadPath(Guid fileId, bool ensureExists = false)
        {
            return this.GetFilePath(fileId, FilePart.Payload, ensureExists);
        }

        private string GetPayloadFilePath(Guid fileId, PayloadDescriptor descriptor, bool ensureExists = false)
        {
            var extension = DriveFileUtility.GetPayloadFileExtension(descriptor.Key, descriptor.Uid);
            var payloadFileName = $"{DriveFileUtility.GetFileIdForStorage(fileId)}{DriveFileUtility.FileNameSectionDelimiter}{extension}";
            return Path.Combine(GetPayloadPath(fileId, ensureExists), $"{payloadFileName}");
        }

        private void DeleteAllThumbnails(Guid fileId)
        {
            var thumbnailSearchPattern = string.Format(ThumbnailSuffixFormatSpecifier, "*", "*");
            var seekPath = this.GetFilename(fileId, thumbnailSearchPattern, FilePart.Thumb);
            string dir = GetFilePath(fileId, FilePart.Thumb);

            if (Directory.Exists(dir))
            {
                var thumbnails = Directory.GetFiles(dir, seekPath);
                foreach (var thumbnail in thumbnails)
                {
                    File.Delete(thumbnail);
                }
            }
        }
    }
}