using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Job
{
    public interface IOutboxJobSystemHttpClient
    {
        private const string Endpoint = $"{OwnerApiPathConstants.PeerV1}/outbox/processor";

        [Post($"{Endpoint}/process-async")]
        Task<ApiResponse<HttpContent>> ProcessOutboxAsync();
    }
}

