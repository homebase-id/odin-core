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
    public interface ITransitTestInboxHttpClient
    {
        private const string RootEndpoint = "/api/apps/v1/transit/inbox";
        
        [Get(RootEndpoint)]
        Task<ApiResponse<PagedResult<TransferBoxItem>>> GetInboxItems(int pageNumber, int pageSize);

        [Get(RootEndpoint + "/item")]
        Task<ApiResponse<TransferBoxItem>> GetInboxItem(Guid id);

        [Delete(RootEndpoint + "/item")]
        Task<ApiResponse<bool>> RemoveInboxItem(Guid id);

        [Get(RootEndpoint)]
        Task<ApiResponse<bool>> ProcessIncoming();
    }
}