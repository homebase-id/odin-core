using System;
using System.IO;
using System.Security.Principal;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Quarantine;
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

        public async Task WritePartStream(Guid id, FilePart filePart, Stream stream)
        {
            var buffer = new byte[WriteChunkSize];
            var bytesRead = 0;

            string filePath = GetFilePath(id, filePart, true);

            await using var output = new FileStream(filePath, FileMode.Append);
            do
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                output.Write(buffer, 0, bytesRead);
            } while (bytesRead > 0);
        }

        public Task WriteKeyHeader(Guid id, Stream stream)
        {
            throw new NotImplementedException();
        }

        public Task<Stream> GetFilePartStream(Guid fileId, FilePart filePart)
        {
            return Task.FromResult((Stream) File.OpenRead(GetFilePath(fileId, filePart)));
        }

        public async Task<KeyHeader> GetKeyHeader(Guid fileId)
        {
            using var stream = File.OpenText(GetFilePath(fileId, FilePart.Header));
            return JsonConvert.DeserializeObject<KeyHeader>(await stream.ReadToEndAsync());
        }

        public void AssertFileIsValid(Guid fileId)
        {
            string header = GetFilePath(fileId, FilePart.Header);
            string metadata = GetFilePath(fileId, FilePart.Metadata);
            string payload = GetFilePath(fileId, FilePart.Payload);
            
            if (!File.Exists(header) || !File.Exists(metadata) || !File.Exists(payload))
            {
                throw new Exception("File does not contain all parts");
            }
        }

        public Task Delete(Guid fileId)
        {
            string header = GetFilePath(fileId, FilePart.Header);
            if (File.Exists(header))
            {
                File.Delete(header);    
            }
            
            string metadata = GetFilePath(fileId, FilePart.Metadata);
            if (File.Exists(metadata))
            {
                File.Delete(metadata);    
            }
            
            string payload = GetFilePath(fileId, FilePart.Payload);

            if (File.Exists(payload))
            {
                File.Delete(payload);    
            }

            return Task.CompletedTask;
        }

        public async Task<long> GetFileSize(Guid id)
        {
            //TODO: make more efficient by reading metadata or something else?
            var path = GetFilePath(id, FilePart.Payload);
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

        public async Task<Guid> SaveMedia(MediaMetaData metaData, Stream stream, bool giveNewId = false)
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
            await fs.ReadAsync(bytes, 0, (int) fs.Length);

            return new MediaData()
            {
                Id = fileRecord.Id,
                MimeType = fileRecord.MimeType,
                Bytes = bytes
            };
        }

        public async Task<MediaMetaData> GetMetaData(Guid id)
        {
            return await _storage.Get(id);
        }

        public async Task<Stream> GetMediaStream(Guid id)
        {
            Console.WriteLine($"Streaming media for ID: [{id}]");
            string path = GetMediaFilePath(id);
            if (File.Exists(path) == false)
            {
                return null;
            }

            return File.OpenRead(path);
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

        private string GetFilePath(Guid id, FilePart part, bool ensureExists = false)
        {
            string dir = PathUtil.Combine(Context.StorageConfig.DataStoragePath, id.ToString());

            if (ensureExists)
            {
                Directory.CreateDirectory(dir);
            }

            return PathUtil.Combine(dir, part.ToString());
        }
    }
}