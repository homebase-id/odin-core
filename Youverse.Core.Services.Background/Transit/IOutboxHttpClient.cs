using System.Threading.Tasks;
using Refit;

namespace Youverse.Core.Services.Workers.Transit
{
    public interface IOutboxHttpClient
    {
        private const string TransitRootEndpoint = "/api/apps/v1/transit";

        [Post(TransitRootEndpoint + "/outbox/processor/process")]
        Task<ApiResponse<bool>> ProcessOutbox();
    }
}