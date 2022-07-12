using System;
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
        /// <returns>(cursor, file Id List)</returns>
        Task<QueryRecentResult> GetRecent(Guid driveId, QueryParams qp, ResultOptions options);

        /// <summary>
        /// Returns a batch of file Ids
        /// </summary>
        Task<QueryBatchResult> GetBatch(Guid driveId, QueryParams qp, ResultOptions options);

        Task RebuildBackupIndex(Guid driveId);

        Task RebuildAllIndices();
        
    }
}