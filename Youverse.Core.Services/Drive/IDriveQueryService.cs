using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Drive.Query;

namespace Youverse.Core.Services.Drive
{
    public class QueryBatchResult
    {
        public byte[] StartCursor { get; set; }
        public byte[] StopCursor { get; set; }
        public UInt64 CursorUpdatedTimestamp { get; set; }
        public IEnumerable<DriveSearchResult> SearchResults { get; set; }
    }
    
    /// <summary>
    /// Access to configured drives, their files
    /// </summary>
    public interface IDriveQueryService
    {
        /// <summary>
        /// Returns the fileId of recently modified files
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="maxDate"></param>
        /// <param name="startCursor"></param>
        /// <param name="qp"></param>
        /// <param name="options"></param>
        /// <returns>(cursor, file Id List)</returns>
        Task<QueryBatchResult> GetRecent(Guid driveId, UInt64 maxDate, byte[] startCursor, QueryParams qp, ResultOptions options);

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
        Task<QueryBatchResult> GetBatch(Guid driveId, QueryParams qp, ResultOptions options);
        
        Task RebuildBackupIndex(Guid driveId);

        Task RebuildAllIndices();
    }
}