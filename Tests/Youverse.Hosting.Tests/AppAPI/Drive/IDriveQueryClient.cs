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
        Task<ApiResponse<PagedResult<IndexedItem>>> GetByTag(Guid tag, bool includeContent, int pageNumber, int pageSize);

        [Get(RootPath + "/recent")]
        Task<ApiResponse<PagedResult<IndexedItem>>> GetRecentlyCreatedItems(bool includeContent, int pageNumber, int pageSize);
        
    }
}