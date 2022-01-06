using System;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Drive.Query.LiteDb;
using Youverse.Core.Services.Drive.Storage;

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
        IndexReadyState IndexReadyState { get; set; }


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


        /// <summary>
        /// Switches from the current index in use to the backup index.  Use after a rebuild
        /// </summary>
        /// <returns></returns>
        Task SwitchIndex();

        /// <summary>
        /// Updates the current index that is in use.
        /// </summary>
        Task UpdateCurrentIndex(FileMetadata metadata);

        /// <summary>
        /// Updates the index that is not currently in use.  Use when performing a rebuild.
        /// </summary>
        /// <param name="metadata"></param>
        Task UpdateSecondaryIndex(FileMetadata metadata);

        /// <summary>
        /// Prepares backup index for rebuild; clears and instantiates a new instance.
        /// </summary>
        /// <returns></returns>
        Task PrepareSecondaryIndexForRebuild();

        Task LoadLatestIndex();

    }
}