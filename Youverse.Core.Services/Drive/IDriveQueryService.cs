using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Drive.Query;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Access to configured drives, their files
    /// </summary>
    public interface IDriveQueryService
    {
        /// <summary>
        /// Returns the fileId of recently modified files
        /// </summary>
        /// <param name="startCursor"></param>
        /// <param name="qp"></param>
        /// <param name="options"></param>
        /// <param name="driveId"></param>
        /// <param name="maxDate"></param>
        /// <returns>(cursor, file Id List)</returns>
        Task<(byte[], IEnumerable<DriveSearchResult>)> GetRecent(Guid driveId, UInt64 maxDate, byte[] startCursor, QueryParams qp, ResultOptions options);


        /// <summary>
        /// Returns a batch of file Ids
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="startCursor"></param>
        /// <param name="stopCursor"></param>
        /// <param name="qp"></param>
        /// <param name="options"></param>
        /// <returns>
        /// (resultFirstCursor, resultLastCursor, cursorUpdatedTimestamp, fileId List);
        /// </returns>
        Task<(byte[], byte[], UInt64, IEnumerable<DriveSearchResult>)> GetBatch(Guid driveId, byte[] startCursor, byte[] stopCursor, QueryParams qp, ResultOptions options);

        Task<PagedResult<DriveSearchResult>> GetRecentlyCreatedItems(Guid driveId, bool includeMetadataHeader, bool includePayload, PageOptions pageOptions);

        Task<PagedResult<DriveSearchResult>> GetByFileType(Guid driveId, int fileType, bool includeMetadataHeader, bool includePayload, PageOptions pageOptions);

        Task<PagedResult<DriveSearchResult>> GetByTag(Guid driveId, Guid tag, int fileType, bool includeMetadataHeader, bool includePayload, PageOptions pageOptions);

        Task<PagedResult<DriveSearchResult>> GetByAlias(Guid driveId, Guid alias, bool includeMetadataHeader, bool includePayload, PageOptions pageOptions);

        Task RebuildBackupIndex(Guid driveId);

        Task RebuildAllIndices();
    }
}