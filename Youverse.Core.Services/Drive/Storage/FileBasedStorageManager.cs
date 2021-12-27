using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Drive.Storage
{
    public class FileBasedStorageManager : IStorageManager
    {
        private readonly ILogger<IStorageManager> _logger;

        private readonly StorageDrive _drive;
        private const int WriteChunkSize = 1024;
        
        public FileBasedStorageManager(StorageDrive drive, ILogger<IStorageManager> logger)
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

        public Task WritePartStream(Guid fileId, FilePart part, Stream stream, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            //TODO: this is probably highly inefficient and probably need to revisit 
            string filePath = GetFilenameAndPath(fileId, part, storageDisposition, true);
            string tempFilePath = GetTempFilePath(fileId, part, storageDisposition);
            try
            {
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

        public Task<Stream> GetFilePartStream(Guid fileId, FilePart filePart, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            string path = GetFilenameAndPath(fileId, filePart, storageDisposition);
            var fileStream = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
            return Task.FromResult((Stream)fileStream);
        }

        public Task<StorageDisposition> GetStorageType(Guid fileId)
        {
            //just check for the header, this assumes the file is valid
            var longTermPath = GetFilenameAndPath(fileId, FilePart.Header, StorageDisposition.LongTerm);

            if (File.Exists(longTermPath))
            {
                return Task.FromResult(StorageDisposition.LongTerm);
            }

            var tempPath = GetFilenameAndPath(fileId, FilePart.Header, StorageDisposition.Temporary);
            if (File.Exists(tempPath))
            {
                return Task.FromResult(StorageDisposition.Temporary);
            }

            return Task.FromResult(StorageDisposition.Unknown);
        }

        public async Task<EncryptedKeyHeader> GetKeyHeader(Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            await using var stream = File.Open(GetFilenameAndPath(fileId, FilePart.Header, storageDisposition), FileMode.Open, FileAccess.Read);
            var json = await new StreamReader(stream).ReadToEndAsync();
            stream.Close();

            var ekh = JsonConvert.DeserializeObject<EncryptedKeyHeader>(json);
            return ekh;
        }

        public async Task WriteKeyHeader(Guid fileId, EncryptedKeyHeader keyHeader, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            var json = JsonConvert.SerializeObject(keyHeader);
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            await this.WritePartStream(fileId, FilePart.Header, stream, storageDisposition);
            stream.Close();
        }

        public void AssertFileIsValid(Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            if (fileId == Guid.Empty)
            {
                throw new Exception("No file specified");
            }

            //check both
            if (storageDisposition == StorageDisposition.Unknown)
            {
                if (IsFileValid(fileId, StorageDisposition.LongTerm))
                {
                    return;
                }

                if (IsFileValid(fileId, StorageDisposition.Temporary))
                {
                    return;
                }
            }

            if (!IsFileValid(fileId, storageDisposition))
            {
                throw new Exception("File does not contain all parts");
            }
        }

        private bool IsFileValid(Guid fileId, StorageDisposition storageDisposition)
        {
            string header = GetFilenameAndPath(fileId, FilePart.Header, storageDisposition);
            string metadata = GetFilenameAndPath(fileId, FilePart.Metadata, storageDisposition);
            string payload = GetFilenameAndPath(fileId, FilePart.Payload, storageDisposition);

            return File.Exists(header) && File.Exists(metadata) && File.Exists(payload);
        }

        public Task Delete(Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            string header = GetFilenameAndPath(fileId, FilePart.Header, storageDisposition);
            if (File.Exists(header))
            {
                File.Delete(header);
            }

            string metadata = GetFilenameAndPath(fileId, FilePart.Metadata, storageDisposition);
            if (File.Exists(metadata))
            {
                File.Delete(metadata);
            }

            string payload = GetFilenameAndPath(fileId, FilePart.Payload, storageDisposition);

            if (File.Exists(payload))
            {
                File.Delete(payload);
            }

            return Task.CompletedTask;
        }

        public Task MoveToLongTerm(Guid fileId)
        {
            AssertFileIsValid(fileId, StorageDisposition.Temporary);

            var parts = Enum.GetNames<FilePart>();
            foreach (var p in parts)
            {
                FilePart part = Enum.Parse<FilePart>(p);
                var source = GetFilenameAndPath(fileId, part, StorageDisposition.Temporary);
                var dest = GetFilenameAndPath(fileId, part, StorageDisposition.LongTerm, ensureExists: true);

                File.Move(source, dest);

                _logger.LogInformation($"File Moved to {dest}");
            }

            return Task.CompletedTask;
        }

        public Task MoveToTemp(Guid fileId)
        {
            var parts = Enum.GetNames<FilePart>();
            foreach (var p in parts)
            {
                FilePart part = Enum.Parse<FilePart>(p);
                var source = GetFilenameAndPath(fileId, part, StorageDisposition.LongTerm);
                var dest = GetFilenameAndPath(fileId, part, StorageDisposition.Temporary, ensureExists: true);
                File.Move(source, dest);
            }

            return Task.CompletedTask;
        }

        public Task<long> GetFileSize(Guid id, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            //TODO: make more efficient by reading metadata or something else?
            var path = GetFilenameAndPath(id, FilePart.Payload, storageDisposition);
            return Task.FromResult(new FileInfo(path).Length);
        }

        public async Task<IEnumerable<FileMetaData>> GetMetadataFiles(PageOptions pageOptions)
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

            var results = new List<FileMetaData>();
            var filePaths = Directory.EnumerateFiles(path, $"*.{FilePart.Metadata.ToString().ToLower()}", options);
            foreach (string filePath in filePaths)
            {
                string filename = Path.GetFileNameWithoutExtension(filePath);
                Guid fileId = Guid.Parse(filename);
                var md = await this.GetMetadata(fileId, StorageDisposition.LongTerm);
                results.Add(md);
            }

            return results;
        }

        public async Task<FileMetaData> GetMetadata(Guid fileId, StorageDisposition storageDisposition)
        {
            var stream = await this.GetFilePartStream(fileId, FilePart.Metadata, storageDisposition);
            var json = await new StreamReader(stream).ReadToEndAsync();
            stream.Close();
            var metadata = JsonConvert.DeserializeObject<FileMetaData>(json);
            return metadata;
        }

        private string GetFileDirectory(Guid fileId, StorageDisposition storageDisposition, bool ensureExists = false)
        {
            string path = _drive.GetStoragePath(storageDisposition);

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

        private string GetFilenameAndPath(Guid fileId, FilePart part, StorageDisposition storageDisposition, bool ensureExists = false)
        {
            string dir = GetFileDirectory(fileId, storageDisposition, ensureExists);
            return Path.Combine(dir, GetFilename(fileId,part));
        }

        private string GetTempFilePath(Guid id, FilePart part, StorageDisposition storageDisposition, bool ensureExists = false)
        {
            string dir = GetFileDirectory(id, storageDisposition, ensureExists);
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