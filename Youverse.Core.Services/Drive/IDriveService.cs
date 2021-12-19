using System;
using System.Threading.Tasks;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Access to configured drives, their files
    /// </summary>
    public interface IDriveService
    {
        Task RebuildAllIndices();

        /// <summary>
        /// Rebuilds the index for a given Drive
        /// </summary>
        /// <param name="driveId"></param>
        /// <returns></returns>
        Task RebuildIndex(Guid driveId);

        IStorageManager StorageManager { get; }

        Task<PagedResult<IndexedItem>> GetRecentlyCreatedItems(Guid driveId, bool includeContent, PageOptions pageOptions);

        Task<PagedResult<IndexedItem>> GetItemsByCategory(Guid driveId, Guid categoryId, bool includeContent, PageOptions pageOptions);
    }
}