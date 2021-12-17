using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.SystemStorage;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Drive.Storage
{
    public class FileBasedStorageManager : IStorageManager, IDisposable
    {
        private readonly ILogger<FileBasedStorageManager> _logger;

        private readonly DotYouContext _context;
        
        private const int WriteChunkSize = 1024;
        private const string CollectionName = "md";

        public FileBasedStorageManager(DotYouContext context, ILogger<FileBasedStorageManager> logger) 
        {
            _context = context;
            _logger = logger;
            string path = PathUtil.Combine(context.StorageConfig.DataStoragePath);
        }

        public Guid CreateId()
        {
            //TODO: Create a date-based
            return Guid.NewGuid();
        }

        public async Task WritePartStream(Guid id, FilePart filePart, Stream stream, StorageType storageType = StorageType.LongTerm)
        {
            var buffer = new byte[WriteChunkSize];
            var bytesRead = 0;

            string filePath = GetFilePath(id, filePart, storageType, true);

            await using var output = new FileStream(filePath, FileMode.Append);
            do
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                output.Write(buffer, 0, bytesRead);
            } while (bytesRead > 0);
        }

        public Task<Stream> GetFilePartStream(Guid fileId, FilePart filePart, StorageType storageType = StorageType.LongTerm)
        {
            return Task.FromResult((Stream)File.OpenRead(GetFilePath(fileId, filePart, storageType)));
        }

        public Task<StorageType> GetStorageType(Guid fileId)
        {
            //just check for the header, this assumes the file is valid
            var longTermPath = GetFilePath(fileId, FilePart.Header, StorageType.LongTerm);

            if (File.Exists(longTermPath))
            {
                return Task.FromResult(StorageType.LongTerm);
            }

            var tempPath = GetFilePath(fileId, FilePart.Header, StorageType.Temporary);
            if (File.Exists(tempPath))
            {
                return Task.FromResult(StorageType.Temporary);
            }

            return Task.FromResult(StorageType.Unknown);
        }

        public async Task<EncryptedKeyHeader> GetKeyHeader(Guid fileId, StorageType storageType = StorageType.LongTerm)
        {
            using var stream = File.Open(GetFilePath(fileId, FilePart.Header, storageType), FileMode.Open, FileAccess.Read);
            var json = await new StreamReader(stream).ReadToEndAsync();
            var ekh  = JsonConvert.DeserializeObject<EncryptedKeyHeader>(json);

            // var ekh = new EncryptedKeyHeader()
            // {
            //     EncryptionVersion = 1,
            //     Iv = 
            //     Type = EncryptionType.Aes,
            //     Data = ms.ToArray()
            // };

            return ekh;
        }

        public async Task WriteKeyHeader(Guid fileId, EncryptedKeyHeader keyHeader, StorageType storageType = StorageType.LongTerm)
        {
            var json = JsonConvert.SerializeObject(keyHeader);
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            await this.WritePartStream(fileId, FilePart.Header, stream, storageType);
        }

        public void AssertFileIsValid(Guid fileId, StorageType storageType = StorageType.LongTerm)
        {
            if (fileId == Guid.Empty)
            {
                throw new Exception("Invalid transfer, no file specified");
            }

            //check both
            if (storageType == StorageType.Unknown)
            {
                if (IsFileValid(fileId, StorageType.LongTerm))
                {
                    return;
                }

                if (IsFileValid(fileId, StorageType.Temporary))
                {
                    return;
                }
            }

            if (!IsFileValid(fileId, storageType))
            {
                throw new Exception("File does not contain all parts");
            }
        }

        private bool IsFileValid(Guid fileId, StorageType storageType)
        {
            string header = GetFilePath(fileId, FilePart.Header, storageType);
            string metadata = GetFilePath(fileId, FilePart.Metadata, storageType);
            string payload = GetFilePath(fileId, FilePart.Payload, storageType);

            return File.Exists(header) && File.Exists(metadata) && File.Exists(payload);
        }

        public Task Delete(Guid fileId, StorageType storageType = StorageType.LongTerm)
        {
            string header = GetFilePath(fileId, FilePart.Header, storageType);
            if (File.Exists(header))
            {
                File.Delete(header);
            }

            string metadata = GetFilePath(fileId, FilePart.Metadata, storageType);
            if (File.Exists(metadata))
            {
                File.Delete(metadata);
            }

            string payload = GetFilePath(fileId, FilePart.Payload, storageType);

            if (File.Exists(payload))
            {
                File.Delete(payload);
            }

            return Task.CompletedTask;
        }

        public Task MoveToLongTerm(Guid fileId)
        {
            AssertFileIsValid(fileId, StorageType.Temporary);

            var parts = Enum.GetNames<FilePart>();
            foreach (var p in parts)
            {
                FilePart part = Enum.Parse<FilePart>(p);
                var source = GetFilePath(fileId, part, StorageType.Temporary);
                var dest = GetFilePath(fileId, part, StorageType.LongTerm, ensureExists: true);

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
                var source = GetFilePath(fileId, part, StorageType.LongTerm);
                var dest = GetFilePath(fileId, part, StorageType.Temporary, ensureExists: true);
                File.Move(source, dest);
            }

            return Task.CompletedTask;
        }

        public async Task<long> GetFileSize(Guid id, StorageType storageType = StorageType.LongTerm)
        {
            //TODO: make more efficient by reading metadata or something else?
            var path = GetFilePath(id, FilePart.Payload, storageType);
            return new FileInfo(path).Length;
        }

        public void Dispose()
        {
        }

        private string GetFilePath(Guid id, FilePart part, StorageType storageType, bool ensureExists = false)
        {
            string path = GetStorageRoot(storageType);
            string dir = PathUtil.Combine(path, id.ToString());

            if (ensureExists)
            {
                Directory.CreateDirectory(dir);
            }

            return PathUtil.Combine(dir, part.ToString());
        }

        private string GetStorageRoot(StorageType storageType)
        {
            var path = storageType == StorageType.Temporary ? _context.StorageConfig.TempStoragePath : _context.StorageConfig.DataStoragePath;
            return path;
        }
    }
}