using System.Threading.Tasks;
using Odin.Core.Fluff;
using Odin.Core.Services.Base;
using Odin.Core.Services.EncryptionKeyService;
using Refit;

namespace Odin.Core.Services.Contacts.Circle.Requests
{
    /// <summary>
    /// Sends connection requests and acceptances to other Digital Identities
    /// </summary>
    public interface ICircleNetworkRequestHttpClient
    {
        private const string RootPath = "/api/perimeter";

        [Post(RootPath + "/invitations/connect")]
        Task<ApiResponse<NoResultResponse>> DeliverConnectionRequest([Body] RsaEncryptedPayload request);

        [Post(RootPath + "/invitations/establishconnection")]
        Task<ApiResponse<NoResultResponse>> EstablishConnection([Body] SharedSecretEncryptedPayload requestReply, string authenticationToken64);
    }
}