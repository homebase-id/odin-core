using System;
using System.IO;
using System.Threading.Tasks;

namespace DotYou.Kernel.Services.MediaService
{
    /// <summary>
    /// Handles the storage, retrieval, and management of media
    /// </summary>
    public interface IStorageService
    {
        
        Task<Guid> SaveMedia(MediaData mediaData, bool giveNewId = false);
        
        Task<Guid> SaveMedia(MediaMetaData metaData, Stream stream, bool giveNewId = false);

        Task<MediaData> GetMedia(Guid id);

        Task<MediaMetaData> GetMetaData(Guid id);
        
        Task<Stream> GetMediaStream(Guid id);
    }
}