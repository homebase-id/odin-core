using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Transit.Incoming;

namespace Youverse.Hosting.Tests.AppAPI.Transit
{
    /// <summary>
    /// The interface for 
    /// </summary>
    public interface ITransitTestAppHttpClient
    {
        private const string RootEndpoint = "/api/apps/v1/transit";
        private const string InboxRoot = RootEndpoint + "/inbox";

        [Post(RootEndpoint + "/app/process")]
        Task<ApiResponse<bool>> ProcessTransfers();

        [Get(InboxRoot)]
        Task<ApiResponse<PagedResult<TransferBoxItem>>> GetInboxItems(int pageNumber, int pageSize);

        [Get(InboxRoot + "/item")]
        Task<ApiResponse<TransferBoxItem>> GetInboxItem(Guid id);

        [Delete(InboxRoot + "/item")]
        Task<ApiResponse<bool>> RemoveInboxItem(Guid id);

    }
}