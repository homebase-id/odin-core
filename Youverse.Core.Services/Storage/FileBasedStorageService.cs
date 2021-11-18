using System;
using System.IO;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Transit;
using Youverse.Core.SystemStorage;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Storage
{
    public class FileBasedStorageService : DotYouServiceBase, IStorageService, IDisposable
    {
        private readonly LiteDBSingleCollectionStorage<MediaMetaData> _storage;
        private const int WriteChunkSize = 1024;
        private const string CollectionName = "md";
        private const string MediaRoot = "media";

        public FileBasedStorageService(DotYouContext context, ILogger<FileBasedStorageService> logger) : base(context, logger, null, null)
        {
            string path = PathUtil.Combine(context.StorageConfig.DataStoragePath, MediaRoot);
            _storage = new LiteDBSingleCollectionStorage<MediaMetaData>(logger, path, CollectionName);
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
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);

            var ekh = new EncryptedKeyHeader()
            {
                Type = EncryptionType.Aes,
                Data = ms.ToArray()
            };

            return ekh;
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
                
                Logger.LogInformation($"File Moved to {dest}");
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

        public async Task<Guid> SaveMedia(MediaData mediaData, bool giveNewId = false)
        {
            Logger.LogDebug($"SaveMedia called - size: {mediaData.Bytes.Length}");

            if (!giveNewId && mediaData.Id == Guid.Empty)
            {
                throw new ArgumentException("Id is empty guid.  You must specify giveNewId = true if you pass in an empty guid.");
            }

            var id = giveNewId ? Guid.NewGuid() : mediaData.Id;

            string path = GetMediaFilePath(id);

            if (File.Exists(path))
            {
                throw new Exception($"Media with Id [{id}] already exists.");
            }

            Console.WriteLine($"Saving media to disk");

            await using var fs = File.Create(path);
            await fs.WriteAsync(mediaData.Bytes);

            Console.WriteLine($"Saving metadata");

            await _storage.Save(new MediaMetaData()
            {
                Id = id,
                MimeType = mediaData.MimeType
            });

            Logger.LogDebug($"Image saved:{id}");
            return id;
        }

        public async Task<Guid> SaveMedia(MediaMetaData metaData, Stream stream, bool giveNewId = false, StorageType storageType = StorageType.LongTerm)
        {
            Logger.LogDebug($"SaveMedia - Stream Edition called - size: {stream.Length}");

            if (!giveNewId && metaData.Id == Guid.Empty)
            {
                throw new ArgumentException("Id is not a valid guid (it's empty).  You must specify giveNewId = true if you pass in an empty guid.");
            }

            var id = giveNewId ? Guid.NewGuid() : metaData.Id;

            string path = GetMediaFilePath(id);

            if (File.Exists(path))
            {
                throw new Exception($"Media with Id [{id}] already exists.");
            }

            Console.WriteLine($"Saving media to disk");

            await using var fs = File.Create(path);
            await stream.CopyToAsync(fs);

            Console.WriteLine($"Saving metadata");
            metaData.Id = id; //be sure we use the new id
            await _storage.Save(metaData);

            Logger.LogDebug($"media saved:{id}");
            return id;
        }

        public async Task<MediaData> GetMedia(Guid id)
        {
            var fileRecord = await _storage.Get(id);
            if (null == fileRecord)
            {
                Console.WriteLine($"No file record found with ID [{id}]");
                Logger.LogInformation($"No file record found with ID [{id}]");
                return null;
            }

            string path = GetMediaFilePath(id);
            if (File.Exists(path) == false)
            {
                Console.WriteLine($"Record exists for ID [{id}] but file not found");
                Logger.LogInformation($"Record exists for ID [{id}] but file not found");
                return null;
            }

            await using var fs = File.OpenRead(path);

            Console.WriteLine($"Path opened at [{path}] with len [{fs.Length}]");
            var bytes = new byte[fs.Length];
            await fs.ReadAsync(bytes, 0, (int)fs.Length);

            return new MediaData()
            {
                Id = fileRecord.Id,
                MimeType = fileRecord.MimeType,
                Bytes = bytes
            };
        }

        public async Task<MediaMetaData> GetMetaData(Guid id, StorageType storageType = StorageType.LongTerm)
        {
            return await _storage.Get(id);
        }

        public Task<FileStream> GetMediaStream(Guid id, StorageType storageType = StorageType.LongTerm)
        {
            Console.WriteLine($"Streaming media for ID: [{id}]");
            string path = GetMediaFilePath(id);
            if (File.Exists(path) == false)
            {
                return null;
            }

            return Task.FromResult(File.OpenRead(path));
        }

        public void Dispose()
        {
            _storage?.Dispose();
        }

        private string GetMediaFilePath(Guid id)
        {
            string filename = id.ToString(); //TODO: how to handle extension and mimetype?
            string path = PathUtil.Combine(GetMediaRoot(), filename);
            return path;
        }

        private string GetMediaRoot()
        {
            string path = PathUtil.Combine(Context.StorageConfig.DataStoragePath, MediaRoot);
            Directory.CreateDirectory(path);
            return path;
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
            var path = storageType == StorageType.Temporary ? Context.StorageConfig.TempStoragePath : Context.StorageConfig.DataStoragePath;
            return path;
        }
    }
}