using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Serialization;

namespace Youverse.Core.Services.Drive.Storage
{
    public class FileBasedLongTermStorageManager : ILongTermStorageManager
    {
        private readonly ILogger<ILongTermStorageManager> _logger;

        private readonly StorageDrive _drive;
        private const int WriteChunkSize = 1024;

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
            var bytes = SequentialGuid.CreateGuid();
            return new Guid(bytes);
        }

        public Task WritePartStream(Guid fileId, FilePart part, Stream stream)
        {
            string filePath = GetFilenameAndPath(fileId, part, true);
            string tempFilePath = GetTempFilePath(fileId, part, null);
            return WriteFile(filePath, tempFilePath, stream);
        }

        public Task<Stream> GetFilePartStream(Guid fileId, FilePart filePart)
        {
            string path = GetFilenameAndPath(fileId, filePart);
            if(File.Exists(path))
            {
                var fileStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return Task.FromResult((Stream)fileStream);
            }

            return Task.FromResult(Stream.Null);
        }

        public Task<Stream> GetThumbnail(Guid fileId, int width, int height)
        {
            string fileName = GetThumbnailFileName(fileId, width, height);
            string dir = GetFilePath(fileId, false);
            string path = Path.Combine(dir, fileName);

            var fileStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Task.FromResult((Stream)fileStream);
        }

        private string GetThumbnailFileName(Guid fileId, int width, int height)
        {
            string suffix = $"-{width}x{height}";
            string fileName = this.GetFilename(fileId, suffix, FilePart.Thumb);
            return fileName;
        }

        public void AssertFileIsValid(Guid fileId)
        {
            if (fileId == Guid.Empty)
            {
                throw new Exception("No file specified");
            }

            if (!IsFileValid(fileId))
            {
                throw new Exception("File does not contain all parts");
            }
        }

        public bool FileExists(Guid fileId)
        {
            return IsFileValid(fileId);
        }

        private bool IsFileValid(Guid fileId)
        {
            string metadata = GetFilenameAndPath(fileId, FilePart.Header);
            string payload = GetFilenameAndPath(fileId, FilePart.Payload);

            return File.Exists(metadata) && File.Exists(payload);
        }

        public Task Delete(Guid fileId)
        {
            string metadata = GetFilenameAndPath(fileId, FilePart.Header);
            if (File.Exists(metadata))
            {
                File.Delete(metadata);
            }

            string payload = GetFilenameAndPath(fileId, FilePart.Payload);

            if (File.Exists(payload))
            {
                File.Delete(payload);
            }

            return Task.CompletedTask;
        }

        public Task MoveToLongTerm(Guid fileId, string sourcePath, FilePart part)
        {
            var dest = GetFilenameAndPath(fileId, part, ensureDirectoryExists: true);
            File.Move(sourcePath, dest, true);
            _logger.LogInformation($"File Moved to {dest}");
            return Task.CompletedTask;
        }

        public Task MoveThumbnailToLongTerm(Guid fileId, string sourceThumbnail, int width, int height)
        {
            var dest = GetThumbnailPath(fileId, width, height);
            File.Move(sourceThumbnail, dest, true);
            _logger.LogInformation($"File Moved to {dest}");

            return Task.CompletedTask;
        }

        public Task<long> GetPayloadFileSize(Guid id)
        {
            //TODO: make more efficient by reading metadata or something else?
            var path = GetFilenameAndPath(id, FilePart.Payload);
            if (!System.IO.File.Exists(path))
            {
                return Task.FromResult((long)0);
            }

            return Task.FromResult(new FileInfo(path).Length);
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
            var json = await new StreamReader(stream).ReadToEndAsync();
            stream.Close();
            var header = DotYouSystemSerializer.Deserialize<ServerFileHeader>(json);
            return header;
        }

        public Task WriteThumbnail(Guid fileId, int width, int height, Stream stream)
        {
            var thumbnailPath = GetThumbnailPath(fileId, width, height);
            var tempPath = GetTempFilePath(fileId, FilePart.Thumb, $"-{width}x{height}", true);

            return WriteFile(thumbnailPath, tempPath, stream);
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
            string dir = GetFilePath(fileId, ensureDirectoryExists);
            return Path.Combine(dir, GetFilename(fileId, string.Empty, part));
        }

        private string GetTempFilePath(Guid fileId, FilePart part, string suffix, bool ensureExists = false)
        {
            string dir = GetFilePath(fileId, ensureExists);
            string filename = $"{Guid.NewGuid()}{part}{suffix}.tmp";
            return Path.Combine(dir, filename);
        }

        public Task<Int64> GetFileCount()
        {
            //TODO: This really won't perform
            var dirs = Directory.GetDirectories(_drive.GetStoragePath(StorageDisposition.LongTerm));
            return Task.FromResult(dirs.LongLength);
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

                output.Close();
            }
        }
    }
}