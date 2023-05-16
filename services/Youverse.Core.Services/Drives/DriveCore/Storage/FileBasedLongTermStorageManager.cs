using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Drives.FileSystem.Base;

namespace Youverse.Core.Services.Drives.DriveCore.Storage
{
    public class FileBasedLongTermStorageManager : ILongTermStorageManager
    {
        private readonly ILogger<ILongTermStorageManager> _logger;

        private readonly StorageDrive _drive;

        private const int WriteChunkSize = 1024;

        // private const string ThumbnailDelimiter = "-";
        private const string ThumbnailDelimiter = "_";
        private const string ThumbnailSizeDelimiter = "x";
        private static readonly string ThumbnailSuffixFormatSpecifier = $"{ThumbnailDelimiter}{{0}}{ThumbnailSizeDelimiter}{{1}}";

        public FileBasedLongTermStorageManager(StorageDrive drive, ILogger<ILongTermStorageManager> logger)
        {
            Guard.Argument(drive, nameof(drive)).NotNull();
            // Guard.Argument(drive, nameof(drive)).Require(sd => Directory.Exists(sd.LongTermDataRootPath), sd => $"No directory for drive storage at {sd.LongTermDataRootPath}");
            // Guard.Argument(drive, nameof(drive)).Require(sd => Directory.Exists(sd.TempDataRootPath), sd => $"No directory for drive storage at {sd.TempDataRootPath}");

            drive.EnsureDirectories();

            _logger = logger;
            _drive = drive;
        }

        public StorageDrive Drive => _drive;

        public Guid CreateFileId()
        {
            return SequentialGuid.CreateGuid();
        }

        public Task WritePartStream(Guid fileId, FilePart part, Stream stream)
        {
            string filePath = GetFilenameAndPath(fileId, part, true);
            string tempFilePath = GetTempFilePath(fileId, part, null);
            return WriteFile(filePath, tempFilePath, stream);
        }

        public Task DeleteFilePartStream(Guid fileId, FilePart filePart)
        {
            string path = GetFilenameAndPath(fileId, filePart);
            File.Delete(path);
            return Task.CompletedTask;
        }

        public Task<Stream> GetFilePartStream(Guid fileId, FilePart filePart, FileChunk chunk = null)
        {
            string path = GetFilenameAndPath(fileId, filePart);
            var fileStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            if (null != chunk)
            {
                var buffer = new byte[chunk.Length];
                if (chunk.Start > fileStream.Length)
                {
                    throw new YouverseClientException("Chunk start position is greater than length", YouverseClientErrorCode.InvalidChunkStart);
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

        public Task<Stream> GetThumbnail(Guid fileId, int width, int height)
        {
            string fileName = GetThumbnailFileName(fileId, width, height);
            string dir = GetFilePath(fileId, false);
            string path = Path.Combine(dir, fileName);
            var fileStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Task.FromResult((Stream)fileStream);
        }

        public Task DeleteThumbnail(Guid fileId, int width, int height)
        {
            string fileName = GetThumbnailFileName(fileId, width, height);
            string dir = GetFilePath(fileId, false);
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

        public void AssertFileIsValid(Guid fileId)
        {
            if (fileId == Guid.Empty)
            {
                throw new YouverseClientException("No file specified", YouverseClientErrorCode.UnknownId);
            }

            if (!IsFileValid(fileId))
            {
                throw new YouverseClientException("File does not contain all parts", YouverseClientErrorCode.MissingUploadData);
            }
        }

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
            if (!header.FileMetadata.AppData.ContentIsComplete) //there should be a payload
            {
                //check for payload
                string payload = GetFilenameAndPath(fileId, FilePart.Payload);
                return File.Exists(payload);
            }

            return true;
        }

        public Task HardDelete(Guid fileId)
        {
            DeleteAllThumbnails(fileId);
            DeletePayload(fileId);

            string metadata = GetFilenameAndPath(fileId, FilePart.Header);
            if (File.Exists(metadata))
            {
                File.Delete(metadata);
            }

            return Task.CompletedTask;
        }

        public Task SoftDelete(Guid fileId)
        {
            DeleteAllThumbnails(fileId);
            DeletePayload(fileId);
            return Task.CompletedTask;
        }

        public Task MoveToLongTerm(Guid targetFileId, string sourcePath, FilePart part)
        {
            var dest = GetFilenameAndPath(targetFileId, part, ensureDirectoryExists: true);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));

            File.Move(sourcePath, dest, true);
            _logger.LogInformation($"File Moved to {dest}");
            return Task.CompletedTask;
        }

        public Task MoveThumbnailToLongTerm(Guid targetFileId, string sourceThumbnail, int width, int height)
        {
            var dest = GetThumbnailPath(targetFileId, width, height);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.Move(sourceThumbnail, dest, true);
            _logger.LogInformation($"File Moved to {dest}");

            return Task.CompletedTask;
        }

        public async Task<IEnumerable<ServerFileHeader>> GetServerFileHeaders(PageOptions pageOptions)
        {
            string path = this.Drive.GetStoragePath(StorageDisposition.LongTerm);
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
            var header = DotYouSystemSerializer.Deserialize<ServerFileHeader>(json);
            return header;
        }

        public Task WriteThumbnail(Guid fileId, int width, int height, Stream stream)
        {
            var thumbnailPath = GetThumbnailPath(fileId, width, height);
            var tempPath = GetTempFilePath(fileId, FilePart.Thumb, $"-{width}x{height}", true);

            return WriteFile(thumbnailPath, tempPath, stream);
        }

        public Task ReconcileThumbnailsOnDisk(Guid fileId, List<ImageDataHeader> thumbnailsToKeep)
        {
            Guard.Argument(thumbnailsToKeep, nameof(thumbnailsToKeep)).NotNull();

            var thumbnailSearchPattern = string.Format(ThumbnailSuffixFormatSpecifier, "*", "*");
            var seekPath = this.GetFilename(fileId, thumbnailSearchPattern, FilePart.Thumb);
            string dir = GetFilePath(fileId, false);

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

            return Task.CompletedTask;
        }

        private string GetThumbnailPath(Guid fileId, int width, int height)
        {
            var thumbnailFileName = GetThumbnailFileName(fileId, width, height);
            var filePath = GetFilePath(fileId);
            var thumbnailPath = Path.Combine(filePath, thumbnailFileName);
            return thumbnailPath;
        }

        private string GetFilePath(Guid fileId, bool ensureExists = false)
        {
            string path = _drive.GetStoragePath(StorageDisposition.LongTerm);

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
            if (part == FilePart.Payload)
            {
                
            }
            
            string dir = GetFilePath(fileId, ensureDirectoryExists);
            return Path.Combine(dir, GetFilename(fileId, string.Empty, part));
        }

        private string GetTempFilePath(Guid fileId, FilePart part, string suffix, bool ensureExists = false)
        {
            string dir = GetFilePath(fileId, ensureExists);
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
            var bytesRead = 0;

            using (var output = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                do
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                    output.Write(buffer, 0, bytesRead);
                } while (bytesRead > 0);

                // stream.Close();
                output.Close();
            }
        }

        private void DeletePayload(Guid fileId)
        {
            string payload = GetFilenameAndPath(fileId, FilePart.Payload);
            if (File.Exists(payload))
            {
                File.Delete(payload);
            }
        }

        private void DeleteAllThumbnails(Guid fileId)
        {
            var thumbnailSearchPattern = string.Format(ThumbnailSuffixFormatSpecifier, "*", "*");
            var seekPath = this.GetFilename(fileId, thumbnailSearchPattern, FilePart.Thumb);
            string dir = GetFilePath(fileId, false);

            var thumbnails = Directory.GetFiles(dir, seekPath);
            foreach (var thumbnail in thumbnails)
            {
                File.Delete(thumbnail);
            }
        }
    }
}