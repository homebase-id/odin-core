using System;
using System.Threading.Tasks;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive.Query;

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

        Task RebuildBackupIndex(Guid driveId);

        Task RebuildAllIndices();

        Task<ClientFileHeader> GetFileByGlobalTransitId(Guid driveId, Guid globalTransitId);
    }
}