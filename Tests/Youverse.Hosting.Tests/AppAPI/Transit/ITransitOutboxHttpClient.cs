using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Transit.Audit;
using Youverse.Core.Services.Transit.Outbox;

namespace Youverse.Hosting.Tests.AppAPI.Transit
{
    public interface ITransitOutboxHttpClient
    {
        private const string RootEndPoint = "/api/apps/v1/transit/outbox";
     
        [Post(RootEndPoint + "/processor/process")]
        Task<ApiResponse<bool>> ProcessOutbox();

        [Get(RootEndPoint)]
        Task<ApiResponse<PagedResult<OutboxItem>>> GetOutboxItems(int pageNumber, int pageSize);

        [Get(RootEndPoint + "/item")]
        Task<ApiResponse<OutboxItem>> GetOutboxItem(Guid id);

        [Delete(RootEndPoint + "/item")]
        Task<ApiResponse<bool>> RemoveOutboxItem(Guid id);

        [Put(RootEndPoint + "/item/priority")]
        Task<ApiResponse<bool>> UpdateOutboxItemPriority(Guid id, int priority);

    }
}