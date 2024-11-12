using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Fluff;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Peer;
using Refit;

namespace Odin.Services.Membership.Connections.Requests
{
    /// <summary>
    /// Sends connection requests and acceptances to other Digital Identities
    /// </summary>
    public interface ICircleNetworkRequestHttpClient
    {
        private const string RootPath = PeerApiPathConstants.InvitationsV1;

        [Post(RootPath + "/connect")]
        Task<ApiResponse<HttpContent>> DeliverConnectionRequest([Body] EccEncryptedPayload request);

        [Post(RootPath + "/establishconnection")]
        Task<ApiResponse<NoResultResponse>> EstablishConnection([Body] SharedSecretEncryptedPayload requestReply);
        
    }
}