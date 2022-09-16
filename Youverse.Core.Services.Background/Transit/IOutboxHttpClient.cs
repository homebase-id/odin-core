using System.Threading.Tasks;
using Refit;

namespace Youverse.Core.Services.Workers.Transit
{
    public interface IOutboxHttpClient
    {
        private const string TransitRootEndpoint = "/api/owner/v1/transit/outbox/processor";

        [Post(TransitRootEndpoint + "/process")]
        Task<ApiResponse<bool>> ProcessOutbox();
    }
}