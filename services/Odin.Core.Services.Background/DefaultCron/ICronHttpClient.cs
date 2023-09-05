using System.Threading.Tasks;
using Odin.Core.Services.Authentication.Owner;
using Refit;

namespace Odin.Core.Services.Background.DefaultCron
{
    public interface ICronHttpClient
    {
        private const string TransitRootEndpoint = $"{OwnerApiPathConstants.TransitV1}/outbox/processor";

        [Post(TransitRootEndpoint + "/process")]
        Task<ApiResponse<bool>> ProcessOutbox();
        
    }
}

