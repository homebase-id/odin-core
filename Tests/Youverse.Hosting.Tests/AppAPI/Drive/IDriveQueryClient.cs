using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
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

        [Get(RootPath + "/recent2")]
        Task<ApiResponse<PagedResult<IndexedItem>>> GetRecent(Guid driveAlias, UInt64 maxDate, byte[] startCursor, [Body] QueryParams qp, [Body] ResultOptions options);

        [Get(RootPath + "/batch")]
        Task<ApiResponse<PagedResult<IndexedItem>>> GetBatch(Guid driveAlias, byte[] startCursor, byte[] stopCursor, [Body] QueryParams qp, [Body] ResultOptions options);
    }
}