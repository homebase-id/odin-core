using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Services.Drives.FileSystem.Base;

namespace Odin.Core.Services.Drives.DriveCore.Storage
{
    public class LongTermStorageManager
    {
        private readonly ILogger<LongTermStorageManager> _logger;

        private readonly StorageDrive _drive;

        private const int WriteChunkSize = 1024;

        private const string ThumbnailDelimiter = "_";
        private const string ThumbnailSizeDelimiter = "x";
        private static readonly string ThumbnailSuffixFormatSpecifier = $"{ThumbnailDelimiter}{{0}}{ThumbnailSizeDelimiter}{{1}}";

        public LongTermStorageManager(StorageDrive drive, ILogger<LongTermStorageManager> logger)
        {
            Guard.Argument(drive, nameof(drive)).NotNull();
            // Guard.Argument(drive, nameof(drive)).Require(sd => Directory.Exists(sd.LongTermDataRootPath), sd => $"No directory for drive storage at {sd.LongTermDataRootPath}");
            // Guard.Argument(drive, nameof(drive)).Require(sd => Directory.Exists(sd.TempDataRootPath), sd => $"No directory for drive storage at {sd.TempDataRootPath}");

            drive.EnsureDirectories();

            _logger = logger;
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
        public Task WritePartStream(Guid fileId, FilePart part, Stream stream)
        {
            string filePath = GetFilenameAndPath(fileId, part, true);
            string tempFilePath = GetTempFilePath(fileId, part, null);
            return WriteFile(filePath, tempFilePath, stream);
        }

        public Task DeletePayload(Guid fileId, string key)
        {
            string path = GetPayloadFilePath(fileId, key);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAllPayloads(Guid fileId)
        {
            string path = GetPayloadPath(fileId);
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

        /// <summary>
        /// Gets a read stream for the given <see cref="FilePart"/>
        /// </summary>
        public Task<Stream> GetFilePartStream(Guid fileId, FilePart filePart, FileChunk chunk = null)
        {
            string path = GetFilenameAndPath(fileId, filePart);
            return GetChunkedStream(path, chunk);
        }

        public Task<Stream> GetPayloadStream(Guid fileId, string key, FileChunk chunk = null)
        {
            var path = GetPayloadFilePath(fileId, key);
            return GetChunkedStream(path, chunk);
        }

        public Task<Stream> GetChunkedStream(string path, FileChunk chunk = null)
        {
            if (!File.Exists(path))
            {
                return Task.FromResult(Stream.Null);
            }

            var fileStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            if (null != chunk)
            {
                var buffer = new byte[chunk.Length];
                if (chunk.Start > fileStream.Length)
                {
                    throw new OdinClientException("Chunk start position is greater than length", OdinClientErrorCode.InvalidChunkStart);
                }

                fileStream.Position = chunk.Start;
                var bytesRead = fileStream.Read(buffer);
                fileStream.Close();

                // if(bytesRead == 0) //TODO: handle end of stream?

                //resize if lenght requested was too large (happens if we hit the end of the stream)
                if (bytesRead < buffer.Length)
                {
                    Array.Resize(ref buffer, bytesRead);
                }

                return Task.FromResult((Stream)new MemoryStream(buffer, false));
            }

            return Task.FromResult((Stream)fileStream);
        }


        /// <summary>
        /// Gets a read stream of the thumbnail
        /// </summary>
        /// <param name="fileId"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        public Task<Stream> GetThumbnail(Guid fileId, int width, int height)
        {
            string fileName = GetThumbnailFileName(fileId, width, height);
            string dir = GetFilePath(fileId, FilePart.Thumb);
            string path = Path.Combine(dir, fileName);
            var fileStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Task.FromResult((Stream)fileStream);
        }

        public Task DeleteThumbnail(Guid fileId, int width, int height)
        {
            string fileName = GetThumbnailFileName(fileId, width, height);
            string dir = GetFilePath(fileId, FilePart.Thumb);
            string path = Path.Combine(dir, fileName);

            File.Delete(path);
            return Task.CompletedTask;
        }

        private string GetThumbnailFileName(Guid fileId, int width, int height)
        {
            var suffix = string.Format(ThumbnailSuffixFormatSpecifier, width, height);
            string fileName = this.GetFilename(fileId, suffix, FilePart.Thumb);
            return fileName;
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
            //TODO: this needs to be optimized by getting all files in the folder; then checking the filename exists
            foreach (var d in header.FileMetadata.Payloads)
            {
                var payloadFilePath = GetPayloadFilePath(fileId, d.Key);
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
            DeleteAllPayloads(fileId);

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
            DeleteAllPayloads(fileId);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Moves the specified <param name="sourcePath"></param> to long term storage.
        /// </summary>
        public Task MovePayloadToLongTerm(Guid targetFileId, string key, string sourcePath)
        {
            var dest = GetPayloadFilePath(targetFileId, key, ensureExists: true);
            Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? throw new OdinSystemException("Destination folder was null"));

            File.Move(sourcePath, dest, true);
            _logger.LogInformation($"File Moved to {dest}");
            return Task.CompletedTask;
        }

        public Task MoveThumbnailToLongTerm(Guid targetFileId, string sourceThumbnail, int width, int height)
        {
            var dest = GetThumbnailPath(targetFileId, width, height);
            Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? throw new OdinSystemException("Destination folder was null"));
            File.Move(sourceThumbnail, dest, true);
            _logger.LogInformation($"File Moved to {dest}");

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

        public async Task<ServerFileHeader> GetServerFileHeader(Guid fileId)
        {
            var stream = await this.GetFilePartStream(fileId, FilePart.Header);
            if (stream == Stream.Null)
            {
                stream.Close();
                return null;
            }

            var json = await new StreamReader(stream).ReadToEndAsync();
            stream.Close();
            await stream.DisposeAsync();
            var header = OdinSystemSerializer.Deserialize<ServerFileHeader>(json);
            return header;
        }

        /// <summary>
        /// Removes all thumbnails on disk which are not in the provided list.
        /// </summary>
        public Task DeleteMissingThumbnailFiles(Guid fileId, List<ImageDataHeader> thumbnailsToKeep)
        {
            Guard.Argument(thumbnailsToKeep, nameof(thumbnailsToKeep)).NotNull();

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

                    var keepThumbnail = thumbnailsToKeep.Exists(thumb => thumb.PixelWidth == width && thumb.PixelHeight == height);
                    if (!keepThumbnail)
                    {
                        File.Delete(thumbnailFilePath);
                    }
                }
            }

            return Task.CompletedTask;
        }

        private string GetThumbnailPath(Guid fileId, int width, int height)
        {
            var thumbnailFileName = GetThumbnailFileName(fileId, width, height);
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
            return $"{fileId.ToString()}{suffix}.{part.ToString().ToLower()}";
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

        private string GetPayloadFilePath(Guid fileId, string key, bool ensureExists = false)
        {
            var extension = DriveFileUtility.GetPayloadFileExtension(key);
            return Path.Combine(GetPayloadPath(fileId, ensureExists), $"{fileId.ToString()}{extension}");
        }

        private string GetTempFilePath(Guid fileId, FilePart part, string suffix, bool ensureExists = false)
        {
            string dir = GetFilePath(fileId, part, ensureExists);
            string filename = $"{Guid.NewGuid()}{part}{suffix}.tmp";
            return Path.Combine(dir, filename);
        }

        private Task WriteFile(string targetFilePath, string tempFilePath, Stream stream)
        {
            //TODO: this is probably highly inefficient and probably need to revisit
            try
            {
                //Process: if there's a file, we write to a temp file then rename.
                if (File.Exists(targetFilePath))
                {
                    WriteStream(stream, tempFilePath);
                    lock (targetFilePath)
                    {
                        // File.WriteAllBytes(targetFilePath, stream.ToByteArray());
                        //TODO: need to know if this replace method is faster than renaming files
                        File.Replace(tempFilePath, targetFilePath, null, true);
                    }
                }
                else
                {
                    WriteStream(stream, targetFilePath);
                }
            }
            finally
            {
                //TODO: should clean up the temp file in case of failure?
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }

            return Task.CompletedTask;
        }

        private void WriteStream(Stream stream, string filePath)
        {
            var buffer = new byte[WriteChunkSize];

            using (var output = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var bytesRead = 0;
                do
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                    output.Write(buffer, 0, bytesRead);
                } while (bytesRead > 0);

                // stream.Close();
                output.Close();
            }
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