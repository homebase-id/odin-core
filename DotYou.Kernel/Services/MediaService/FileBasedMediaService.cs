using System;
using System.IO;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.Storage;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.MediaService
{
    public class FileBasedMediaService : DotYouServiceBase, IMediaService, IDisposable
    {
        private readonly LiteDBSingleCollectionStorage<MediaMetaData> _storage;
        const string CollectionName = "MediaData";

        public FileBasedMediaService(DotYouContext context, ILogger<FileBasedMediaService> logger) : base(context, logger, null, null)
        {
            string path = Path.Combine(context.StorageConfig.DataStoragePath, CollectionName);
            _storage = new LiteDBSingleCollectionStorage<MediaMetaData>(logger, path, CollectionName);
        }

        public async Task SaveImage(MediaMetaData metaData, byte[] bytes)
        {
            Console.WriteLine($"SaveImage called - size: {bytes.Length}");
            string path = GetFilePath(metaData.Id);
            await using var fs = File.Create(path);
            await fs.WriteAsync(bytes);
            await _storage.Save(metaData);
        }

        public async Task<MediaResult> GetImage(Guid id)
        {
            var fileRecord = await _storage.Get(id);
            if (null == fileRecord)
            {
                Logger.LogInformation($"No file record found with ID [{id}]");
                return null;
            }
            
            string path = GetFilePath(id);
            if (File.Exists(path) == false)
            {
                Logger.LogInformation($"Record exists for ID [{id}] but file not found");
                return null;
            }

            await using var fs = File.OpenRead(path);
            var bytes = new byte[fs.Length];
            await fs.ReadAsync(bytes, 0, (int)fs.Length);
            
            return new MediaResult()
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
            string path = Path.Combine(Context.StorageConfig.DataStoragePath, "media");
            Directory.CreateDirectory(path);
            return path;
        }
        
    }
}