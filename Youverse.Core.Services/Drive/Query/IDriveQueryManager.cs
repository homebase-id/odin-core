using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualBasic.FileIO;
using Youverse.Core.Services.Authorization.Acl;
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
        /// <param name="includeMetadataHeader">if true, the value of JsonContent will be included in the result.</param>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<IndexedItem>> GetRecentlyCreatedItems(bool includeMetadataHeader, PageOptions pageOptions, IDriveAclAuthorizationService driveAclAuthorizationService);

        /// <summary>
        /// Returns all <see cref="IndexedItem"/>s matching the given category. Items are returned CreateTimestamp descending
        /// </summary>
        /// <param name="tag">The category to match</param>
        /// <param name="fileType">The type of file to match</param>
        /// <param name="includeMetadataHeader">if true, the value of JsonContent will be included in the result.</param>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<IndexedItem>> GetByTag(Guid tag, int fileType, bool includeMetadataHeader, PageOptions pageOptions, IDriveAclAuthorizationService driveAclAuthorizationService);

        /// <summary>
        /// Returns all <see cref="IndexedItem"/>s matching the given category. Items are returned CreateTimestamp descending
        /// </summary>
        /// <param name="tag">The category to match</param>
        /// <param name="includeMetadataHeader">if true, the value of JsonContent will be included in the result.</param>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<IndexedItem>> GetByAlias(Guid alias, bool includeMetadataHeader, PageOptions pageOptions, IDriveAclAuthorizationService driveAclAuthorizationService);


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