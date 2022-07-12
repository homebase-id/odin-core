using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Query.Sqlite.Storage;
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
        /// Returns the fileId of recently modified files
        /// </summary>
        /// <param name="callerContext"></param>
        /// <param name="maxDate"></param>
        /// <param name="cursor"></param>
        /// <param name="qp"></param>
        /// <param name="options"></param>
        /// <returns>(cursor, file Id List)</returns>
        Task<(ulong, IEnumerable<Guid>)> GetRecent(CallerContext callerContext, ulong maxDate, ulong cursor, QueryParams qp, ResultOptions options);


        /// <summary>
        /// Returns a batch of file Ids
        /// </summary>
        /// <returns>
        /// (resultFirstCursor, resultLastCursor, cursorUpdatedTimestamp, fileId List);
        /// </returns>
        Task<(QueryBatchCursor, IEnumerable<Guid>)> GetBatch(CallerContext callerContext, QueryBatchCursor cursor, QueryParams qp, ResultOptions options);

        /// <summary>
        /// Switches from the current index in use to the backup index.  Use after a rebuild
        /// </summary>
        /// <returns></returns>
        Task SwitchIndex();

        /// <summary>
        /// Updates the current index that is in use.
        /// </summary>
        Task UpdateCurrentIndex(ServerFileHeader metadata);

        /// <summary>
        /// Removes the specified file from the index that is currently in use.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        Task RemoveFromCurrentIndex(InternalDriveFileId file);

        /// <summary>
        /// Removes the specified file from the secondary index.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        Task RemoveFromSecondaryIndex(InternalDriveFileId file);

        /// <summary>
        /// Updates the index that is not currently in use.  Use when performing a rebuild.
        /// </summary>
        /// <param name="metadata"></param>
        Task UpdateSecondaryIndex(ServerFileHeader metadata);

        /// <summary>
        /// Prepares backup index for rebuild; clears and instantiates a new instance.
        /// </summary>
        /// <returns></returns>
        Task PrepareSecondaryIndexForRebuild();

        Task LoadLatestIndex();
    }
}