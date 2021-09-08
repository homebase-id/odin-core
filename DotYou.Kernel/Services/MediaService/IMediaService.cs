using System;
using System.Threading.Tasks;

namespace DotYou.Kernel.Services.MediaService
{
    /// <summary>
    /// Handles the storage, retrieval, and management of media
    /// </summary>
    public interface IMediaService
    {
        Task<Guid> SaveImage(MediaData mediaData, bool giveNewId = false);

        Task<MediaData> GetImage(Guid id);
    }
}