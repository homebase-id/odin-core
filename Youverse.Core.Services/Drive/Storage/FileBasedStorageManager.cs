using System;
using System.IO;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Drive.Storage
{
    public class FileBasedStorageManager : IDriveManager
    {
        private readonly ILogger<FileBasedStorageManager> _logger;

        private readonly DotYouContext _context;
        private readonly StorageDrive _drive;
        private const int WriteChunkSize = 1024;

        public FileBasedStorageManager(DotYouContext context, ILogger<FileBasedStorageManager> logger, StorageDrive drive)
        {
            Guard.Argument(drive, nameof(drive)).NotNull();
            Guard.Argument(drive, nameof(drive)).Require(sd => Directory.Exists(sd.LongTermDataRootPath));
            Guard.Argument(drive, nameof(drive)).Require(sd => Directory.Exists(sd.TempDataRootPath));
            
            _context = context;
            _logger = logger;
            _drive = drive;
        }

        public Guid CreateFileId()
        {
            //TODO: Create a date-based
            return Guid.NewGuid();
        }

        public async Task WritePartStream(Guid fileId, FilePart filePart, Stream stream, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            var buffer = new byte[WriteChunkSize];
            var bytesRead = 0;

            string filePath = GetFilePath(fileId, filePart, storageDisposition, true);

            await using var output = new FileStream(filePath, FileMode.Append);
            do
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                output.Write(buffer, 0, bytesRead);
            } while (bytesRead > 0);
        }

        public Task<Stream> GetFilePartStream(Guid fileId, FilePart filePart, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            return Task.FromResult((Stream)File.OpenRead(GetFilePath(fileId, filePart, storageDisposition)));
        }

        public Task<StorageDisposition> GetStorageType(Guid fileId)
        {
            //just check for the header, this assumes the file is valid
            var longTermPath = GetFilePath(fileId, FilePart.Header, StorageDisposition.LongTerm);

            if (File.Exists(longTermPath))
            {
                return Task.FromResult(StorageDisposition.LongTerm);
            }

            var tempPath = GetFilePath(fileId, FilePart.Header, StorageDisposition.Temporary);
            if (File.Exists(tempPath))
            {
                return Task.FromResult(StorageDisposition.Temporary);
            }

            return Task.FromResult(StorageDisposition.Unknown);
        }

        public async Task<EncryptedKeyHeader> GetKeyHeader(Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            using var stream = File.Open(GetFilePath(fileId, FilePart.Header, storageDisposition), FileMode.Open, FileAccess.Read);
            var json = await new StreamReader(stream).ReadToEndAsync();
            var ekh = JsonConvert.DeserializeObject<EncryptedKeyHeader>(json);

            // var ekh = new EncryptedKeyHeader()
            // {
            //     EncryptionVersion = 1,
            //     Iv = 
            //     Type = EncryptionType.Aes,
            //     Data = ms.ToArray()
            // };

            return ekh;
        }

        public async Task WriteKeyHeader(Guid fileId, EncryptedKeyHeader keyHeader, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            var json = JsonConvert.SerializeObject(keyHeader);
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            await this.WritePartStream(fileId, FilePart.Header, stream, storageDisposition);
        }

        public void AssertFileIsValid(Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            if (fileId == Guid.Empty)
            {
                throw new Exception("Invalid transfer, no file specified");
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
            string header = GetFilePath(fileId, FilePart.Header, storageDisposition);
            string metadata = GetFilePath(fileId, FilePart.Metadata, storageDisposition);
            string payload = GetFilePath(fileId, FilePart.Payload, storageDisposition);

            return File.Exists(header) && File.Exists(metadata) && File.Exists(payload);
        }

        public Task Delete(Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            string header = GetFilePath(fileId, FilePart.Header, storageDisposition);
            if (File.Exists(header))
            {
                File.Delete(header);
            }

            string metadata = GetFilePath(fileId, FilePart.Metadata, storageDisposition);
            if (File.Exists(metadata))
            {
                File.Delete(metadata);
            }

            string payload = GetFilePath(fileId, FilePart.Payload, storageDisposition);

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
                var source = GetFilePath(fileId, part, StorageDisposition.Temporary);
                var dest = GetFilePath(fileId, part, StorageDisposition.LongTerm, ensureExists: true);

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
                var source = GetFilePath(fileId, part, StorageDisposition.LongTerm);
                var dest = GetFilePath(fileId, part, StorageDisposition.Temporary, ensureExists: true);
                File.Move(source, dest);
            }

            return Task.CompletedTask;
        }

        public async Task<long> GetFileSize(Guid id, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            //TODO: make more efficient by reading metadata or something else?
            var path = GetFilePath(id, FilePart.Payload, storageDisposition);
            return new FileInfo(path).Length;
        }

        private string GetFilePath(Guid id, FilePart part, StorageDisposition storageDisposition, bool ensureExists = false)
        {
            string path = _drive.GetStoragePath(storageDisposition);
            string dir = PathUtil.Combine(path, id.ToString());

            if (ensureExists)
            {
                Directory.CreateDirectory(dir);
            }

            return PathUtil.Combine(dir, part.ToString());
        }
    }
}