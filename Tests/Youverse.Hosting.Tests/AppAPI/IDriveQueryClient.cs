using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Drive.Query;

namespace Youverse.Hosting.Tests.AppAPI
{
    public interface IDriveQueryClient
    {
        private const string RootPath = "/api/owner/v1/drive/query";

        [Get(RootPath + "/category")]
        Task<ApiResponse<PagedResult<IndexedItem>>> GetItemsByCategory(Guid driveId, Guid categoryId, bool includeContent, int pageNumber, int pageSize);

        [Get(RootPath + "/recent")]
        Task<ApiResponse<PagedResult<IndexedItem>>> GetRecentlyCreatedItems(Guid driveId, bool includeContent, int pageNumber, int pageSize);

    }
}