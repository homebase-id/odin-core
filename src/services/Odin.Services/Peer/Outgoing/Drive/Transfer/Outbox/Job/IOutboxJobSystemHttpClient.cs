using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Job
{
    public interface IOutboxJobSystemHttpClient
    {
        private const string TransitRootEndpoint = $"{OwnerApiPathConstants.PeerV1}/outbox/processor";

        [Post($"{TransitRootEndpoint}/initiate")]
        Task<ApiResponse<HttpContent>> ProcessOutbox();
    }
}

