using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Transit.Inbox;

namespace Youverse.Hosting.Tests.AppAPI.Transit
{
    /// <summary>
    /// The interface for 
    /// </summary>
    public interface ITransitInboxHttpClient
    {
        private const string RootEndpoint = "/api/apps/v1/transit/inbox";
        
        [Get(RootEndpoint)]
        Task<ApiResponse<PagedResult<InboxItem>>> GetInboxItems(int pageNumber, int pageSize);

        [Get(RootEndpoint + "/item")]
        Task<ApiResponse<InboxItem>> GetInboxItem(Guid id);

        [Delete(RootEndpoint + "/item")]
        Task<ApiResponse<bool>> RemoveInboxItem(Guid id);
        
    }
}