using System;
using System.Threading.Tasks;

namespace DotYou.Kernel.Services.MediaService
{
    /// <summary>
    /// Handles the storage, retrieval, and management of media
    /// </summary>
    public interface IMediaService
    {
        Task SaveImage(MediaMetaData metaData, byte[] bytes);

        Task<MediaResult> GetImage(Guid id);
    }
}