using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Services.Transit.Encryption;

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
            var datetime = DateTimeOffset.UtcNow;

            var random = new Random();
            var rnd = new byte[7];
            random.NextBytes(rnd);

            //byte[] offset = BitConverter.GetBytes(datetime.Offset.TotalMinutes);
            var year = BitConverter.GetBytes((short)datetime.Year);
            var bytes = new byte[16]
            {
                (byte)datetime.Day,
                (byte)datetime.Month,
                year[0],
                year[1],
                (byte)datetime.Minute,
                (byte)datetime.Hour,
                (byte)datetime.Second,
                (byte)datetime.Millisecond,
                255, //variant: unknown
                rnd[0],
                rnd[1],
                rnd[2],
                rnd[3],
                rnd[4],
                rnd[5],
                rnd[6]
            };

            // set the version to be compliant with rfc; not sure it matters
            bytes[7] &= 0x0f;
            bytes[7] |= 0x04 << 4;

            return new Guid(bytes);
        }

        public Task WritePartStream(Guid fileId, FilePart part, Stream stream)
        {
            //TODO: this is probably highly inefficient and probably need to revisit 
            string filePath = GetFilenameAndPath(fileId, part, true);
            string tempFilePath = GetTempFilePath(fileId, part);

            try
            {
                //Process: if there's a file, we write to a temp file then rename.
                if (File.Exists(filePath))
                {
                    WriteStream(stream, tempFilePath);
                    lock (filePath)
                    {
                        //TODO: need to know if this replace method is faster than renaming files
                        File.Replace(tempFilePath, filePath, null, true);
                    }
                }
                else
                {
                    WriteStream(stream, filePath);
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

        public Task<Stream> GetFilePartStream(Guid fileId, FilePart filePart)
        {
            string path = GetFilenameAndPath(fileId, filePart);
            var fileStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Task.FromResult((Stream)fileStream);
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
            string metadata = GetFilenameAndPath(fileId, FilePart.Metadata);
            string payload = GetFilenameAndPath(fileId, FilePart.Payload);

            return File.Exists(metadata) && File.Exists(payload);
        }

        public Task Delete(Guid fileId)
        {
            string metadata = GetFilenameAndPath(fileId, FilePart.Metadata);
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

        public Task MoveToLongTerm(Guid fileId, string filePath, FilePart part)
        {
            var dest = GetFilenameAndPath(fileId, part, ensureDirectoryExists: true);
            File.Move(filePath, dest, true);
            _logger.LogInformation($"File Moved to {dest}");
            return Task.CompletedTask;
        }

        public Task<long> GetPayloadFileSize(Guid id)
        {
            //TODO: make more efficient by reading metadata or something else?
            var path = GetFilenameAndPath(id, FilePart.Payload);
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
            var filePaths = Directory.EnumerateFiles(path, $"*.{FilePart.Metadata.ToString().ToLower()}", options);
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
            var stream = await this.GetFilePartStream(fileId, FilePart.Metadata);
            var json = await new StreamReader(stream).ReadToEndAsync();
            stream.Close();
            var header = JsonConvert.DeserializeObject<ServerFileHeader>(json);
            return header;
        }

        private string GetFileDirectory(Guid fileId, bool ensureExists = false)
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

        private string GetFilename(Guid fileId, FilePart part)
        {
            return $"{fileId.ToString()}.{part.ToString().ToLower()}";
        }

        private string GetFilenameAndPath(Guid fileId, FilePart part, bool ensureDirectoryExists = false)
        {
            string dir = GetFileDirectory(fileId, ensureDirectoryExists);
            return Path.Combine(dir, GetFilename(fileId, part));
        }

        private string GetTempFilePath(Guid fileId, FilePart part, bool ensureExists = false)
        {
            string dir = GetFileDirectory(fileId, ensureExists);
            string filename = $"{Guid.NewGuid()}{part}.tmp";
            return Path.Combine(dir, filename);
        }

        public Task<Int64> GetFileCount()
        {
            //TODO: This really won't perform
            var dirs = Directory.GetDirectories(_drive.GetStoragePath(StorageDisposition.LongTerm));
            return Task.FromResult(dirs.LongLength);
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