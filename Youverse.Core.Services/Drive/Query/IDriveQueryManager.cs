using System;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Drive.Query.LiteDb;

namespace Youverse.Core.Services.Drive.Query
{
    /// <summary>
    /// Offers query and indexing features for a specific <see cref="StorageDrive"/>
    /// </summary>
    public interface IDriveQueryManager
    {
        /// <summary>
        /// Specifies the drive being managed (indexed and queried)
        /// </summary>
        StorageDrive Drive { get; init; }

        /// <summary>
        /// 
        /// </summary>
        IndexReadyState IndexReadyState { get; }

        /// <summary>
        /// Loads the latest available index.  After calling, you should check IndexReadyState.
        /// </summary>
        /// <returns></returns>
        Task SetCurrentIndex(StorageDriveIndex index);

        /// <summary>
        /// Returns the most recently created <see cref="IndexedItem"/>s.  Items are returned CreateTimestamp descending
        /// </summary>
        /// <param name="includeContent">if true, the value of JsonContent will be included in the result.</param>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<IndexedItem>> GetRecentlyCreatedItems(bool includeContent, PageOptions pageOptions);

        /// <summary>
        /// Returns all <see cref="IndexedItem"/>s matching the given category. Items are returned CreateTimestamp descending
        /// </summary>
        /// <param name="categoryId">The category to match</param>
        /// <param name="includeContent">if true, the value of JsonContent will be included in the result.</param>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<IndexedItem>> GetItemsByCategory(Guid categoryId, bool includeContent, PageOptions pageOptions);

    }
}