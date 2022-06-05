using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Hosting.Controllers.Apps;

namespace Youverse.Hosting.Tests.AppAPI.Drive
{
    public interface IDriveQueryClient
    {
        private const string RootPath = AppApiPathConstants.DrivesV1 + "/query";

        [Get(RootPath + "/tag")]
        Task<ApiResponse<PagedResult<IndexedItem>>> GetByTag(Guid driveAlias, Guid tag, bool includeContent, int pageNumber, int pageSize);

        [Get(RootPath + "/recent")]
        Task<ApiResponse<PagedResult<IndexedItem>>> GetRecentlyCreatedItems(Guid driveAlias, bool includeContent, int pageNumber, int pageSize);

        [Post(RootPath + "/recent2")]
        Task<ApiResponse<QueryBatchResult>> GetRecent([Query] Guid driveAlias, [Query] UInt64 maxDate, [Query] byte[] startCursor, [Body] QueryParams qp, [Query] ResultOptions options);

        [Post(RootPath + "/batch")]
        Task<ApiResponse<QueryBatchResult>> GetBatch([Query] Guid driveAlias, [Query] byte[] startCursor, [Query] byte[] stopCursor, [Body] QueryParams qp, [Query] ResultOptions options);
    }
}