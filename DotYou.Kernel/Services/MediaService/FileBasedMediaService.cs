using System;
using System.IO;
using System.Threading.Tasks;
using Dawn;
using DotYou.IdentityRegistry;
using DotYou.Kernel.Storage;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.MediaService
{
    public class FileBasedMediaService : DotYouServiceBase, IMediaService, IDisposable
    {
        private readonly LiteDBSingleCollectionStorage<MediaMetaData> _storage;
        const string CollectionName = "md";
        private const string MediaRoot = "media";

        public FileBasedMediaService(DotYouContext context, ILogger<FileBasedMediaService> logger) : base(context, logger, null, null)
        {
            string path = Path.Combine(context.StorageConfig.DataStoragePath, MediaRoot);
            _storage = new LiteDBSingleCollectionStorage<MediaMetaData>(logger, path, CollectionName);
        }

        public async Task<Guid> SaveImage(MediaData mediaData, bool giveNewId = false)
        {
            Logger.LogDebug($"SaveImage called - size: {mediaData.Bytes.Length}");

            if (!giveNewId && mediaData.Id == Guid.Empty)
            {
                throw new ArgumentException("Id is empty guid.  You must specify giveNewId = true if you pass in an empty guid.");
            }
            
            var id = giveNewId ? Guid.NewGuid() : mediaData.Id;
            
            string path = GetFilePath(id);

            if (File.Exists(path))
            {
                throw new Exception($"Media with Id [{id}] already exists.");
            }
            
            Console.WriteLine($"Saving image to disk");
            
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

        public async Task<MediaData> GetImage(Guid id)
        {
            var fileRecord = await _storage.Get(id);
            if (null == fileRecord)
            {
                Console.WriteLine($"No file record found with ID [{id}]");
                Logger.LogInformation($"No file record found with ID [{id}]");
                return null;
            }
            
            string path = GetFilePath(id);
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

        public void Dispose()
        {
            _storage?.Dispose();
        }

        private string GetFilePath(Guid id)
        {
            string filename = id.ToString(); //TODO: how to handle extension and mimetype?
            string path = Path.Combine(GetMediaRoot(), filename);
            return path;
        }

        private string GetMediaRoot()
        {
            string path = Path.Combine(Context.StorageConfig.DataStoragePath, MediaRoot);
            Directory.CreateDirectory(path);
            return path;
        }
        
    }
}