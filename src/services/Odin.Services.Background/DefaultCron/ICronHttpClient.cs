using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Refit;

namespace Odin.Services.Background.DefaultCron
{
    public interface ICronHttpClient
    {
        private const string TransitRootEndpoint = $"{OwnerApiPathConstants.PeerV1}/outbox/processor";

        [Obsolete]
        [Post(TransitRootEndpoint + "/process")]
        Task<ApiResponse<HttpContent>> ProcessOutbox();
        
        [Post($"{TransitRootEndpoint}/reconcile")]
        Task<ApiResponse<HttpContent>> ReconcileInboxOutbox();
    }
}

