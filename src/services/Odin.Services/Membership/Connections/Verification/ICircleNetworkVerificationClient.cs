using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Base;
using Odin.Services.Peer;
using Refit;

namespace Odin.Services.Membership.Connections.Verification
{
    /// <summary>
    /// Sends connection requests and acceptances to other Digital Identities
    /// </summary>
    public interface ICircleNetworkVerificationClient
    {
        private const string RootPath = PeerApiPathConstants.InvitationsV1;

        /// <summary>
        /// Verifies a connection is valid between two identities
        /// </summary>
        [Post(RootPath + "/verify-identity-connection")]
        Task<ApiResponse<VerifyConnectionResponse>> VerifyConnection();
        
        /// <summary>
        /// Verifies a connection is valid between two identities
        /// </summary>
        [Post(RootPath + "/update-remote-verification-hash")]
        Task<ApiResponse<HttpContent>> UpdateRemoteVerificationHash(SharedSecretEncryptedPayload payload);

    }
}