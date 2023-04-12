using System.Threading.Tasks;
using Refit;

namespace Youverse.Core.Services.Workers.DefaultCron
{
    public interface ICronHttpClient
    {
        private const string TransitRootEndpoint = "/api/owner/v1/transit/outbox/processor";

        [Post(TransitRootEndpoint + "/process")]
        Task<ApiResponse<bool>> ProcessOutbox();
        
    }
}

