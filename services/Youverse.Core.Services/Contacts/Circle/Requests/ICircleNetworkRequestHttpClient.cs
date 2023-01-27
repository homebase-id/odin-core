using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.EncryptionKeyService;

namespace Youverse.Core.Services.Contacts.Circle.Requests
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
        Task<ApiResponse<NoResultResponse>> EstablishConnection([Body] RsaEncryptedPayload requestReply);
    }
}