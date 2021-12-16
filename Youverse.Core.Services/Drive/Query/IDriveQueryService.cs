using System;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Drive.Query
{
    /// <summary>
    /// Offers query features for finding data with in storage containers
    /// </summary>
    public interface IDriveQueryService
    {
        /// <summary>
        /// Returns the most recently created <see cref="IndexedItem"/>s.  Items are returned CreateTimestamp descending
        /// </summary>
        /// <param name="driveId">The container to query</param>
        /// <param name="includeContent">if true, the value of JsonContent will be included in the result.</param>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<IndexedItem>> GetRecentlyCreatedItems(Guid driveId, bool includeContent, PageOptions pageOptions);

        /// <summary>
        /// Returns all <see cref="IndexedItem"/>s matching the given category. Items are returned CreateTimestamp descending
        /// </summary>
        /// <param name="driveId">The container to query</param>
        /// <param name="categoryId">The category to match</param>
        /// <param name="includeContent">if true, the value of JsonContent will be included in the result.</param>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<IndexedItem>> GetItemsByCategory(Guid driveId, Guid categoryId, bool includeContent, PageOptions pageOptions);
    }
}