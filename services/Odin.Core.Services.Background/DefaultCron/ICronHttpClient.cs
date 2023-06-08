using System.Threading.Tasks;
using Refit;

namespace Odin.Core.Services.Background.DefaultCron
{
    public interface ICronHttpClient
    {
        private const string TransitRootEndpoint = "/api/owner/v1/transit/outbox/processor";

        [Post(TransitRootEndpoint + "/process")]
        Task<ApiResponse<bool>> ProcessOutbox();
        
    }
}

