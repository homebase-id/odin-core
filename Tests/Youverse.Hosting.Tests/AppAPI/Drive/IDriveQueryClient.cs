using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Drive.Query;

namespace Youverse.Hosting.Tests.AppAPI.Drive
{
    public interface IDriveQueryClient
    {
        private const string RootPath = "/api/owner/v1/drive/query";

        [Get(RootPath + "/category")]
        Task<ApiResponse<PagedResult<IndexedItem>>> GetItemsByCategory(Guid categoryId, bool includeContent, int pageNumber, int pageSize);

        [Get(RootPath + "/recent")]
        Task<ApiResponse<PagedResult<IndexedItem>>> GetRecentlyCreatedItems(bool includeContent, int pageNumber, int pageSize);
    }
}