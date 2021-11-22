using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Transit.Outbox;

namespace Youverse.Core.Services.Transit.Inbox
{
    /// <summary>
    /// The interface for 
    /// </summary>
    public interface ITransitInboxHttpClient
    {
        private const string RootEndpoint = "/api/transit/client/inbox";
        
        [Get(RootEndpoint)]
        Task<ApiResponse<PagedResult<OutboxItem>>> GetInboxItems(int pageNumber, int pageSize);

        [Get(RootEndpoint + "/item")]
        Task<ApiResponse<OutboxItem>> GetInboxItem(Guid id);

        [Delete(RootEndpoint + "/item")]
        Task<ApiResponse<bool>> RemoveInboxItem(Guid id);
        
    }
}