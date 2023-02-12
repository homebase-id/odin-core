using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive.Core.Query;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Access to configured drives, their files
    /// </summary>
    public interface IDriveQueryService
    {
        /// <summary>
        /// Returns a list of files 
        /// </summary>
        Task<QueryModifiedResult> GetModified(Guid driveId, FileQueryParams qp, QueryModifiedResultOptions options);

        /// <summary>
        /// Returns a batch of files matching the params
        /// </summary>
        Task<QueryBatchResult> GetBatch(Guid driveId, FileQueryParams qp, QueryBatchResultOptions options);

        Task<ClientFileHeader> GetFileByGlobalTransitId(Guid driveId, Guid globalTransitId);

        Task<ClientFileHeader> GetFileByClientUniqueId(Guid driveId, Guid clientUniqueId);

        Task<QueryBatchCollectionResponse> GetBatchCollection(QueryBatchCollectionRequest request);

        Task EnsureIndexerCommits(IEnumerable<Guid> driveIdList);
    }
}