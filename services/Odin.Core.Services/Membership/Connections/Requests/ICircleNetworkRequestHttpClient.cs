using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Fluff;
using Odin.Core.Services.Base;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Services.Peer;
using Refit;

namespace Odin.Core.Services.Membership.Connections.Requests
{
    /// <summary>
    /// Sends connection requests and acceptances to other Digital Identities
    /// </summary>
    public interface ICircleNetworkRequestHttpClient
    {
        private const string RootPath = PeerApiPathConstants.InvitationsV1;

        [Post(RootPath + "/connect")]
        Task<ApiResponse<NoResultResponse>> DeliverConnectionRequest([Body] RsaEncryptedPayload request);

        [Post(RootPath + "/establishconnection")]
        Task<ApiResponse<NoResultResponse>> EstablishConnection([Body] SharedSecretEncryptedPayload requestReply, string authenticationToken64);
        
        [Post(RootPath + "/finalizeconnection")]
        Task<ApiResponse<HttpContent>> FinalizeConnection([Body] SharedSecretEncryptedPayload request);
    }
}