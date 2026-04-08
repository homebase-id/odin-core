using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Base;
using Odin.Services.Membership.Connections.Verification;
using Odin.Services.Peer;
using Refit;

namespace Odin.Services.Membership.Connections
{
    /// <summary>
    /// Sends connection requests and acceptances to other Digital Identities
    /// </summary>
    public interface ICircleNetworkPeerConnectionsClient
    {
        private const string RootPath = PeerApiPathConstants.ConnectionsV1;

        /// <summary>
        /// Verifies a connection is valid between two identities
        /// </summary>
        [Post(RootPath + "/verify-identity-connection")]
        Task<ApiResponse<VerifyConnectionResponse>> VerifyConnection(CancellationToken cancellationToken = default);

        /// <summary>
        /// Verifies a connection is valid between two identities
        /// </summary>
        [Post(RootPath + "/update-remote-verification-hash")]
        Task<ApiResponse<SyncRemoteVerificationHashResult>> UpdateRemoteVerificationHash(SharedSecretEncryptedPayload payload, CancellationToken cancellationToken = default);

        /// <summary>
        /// Makes an introduction between two identities
        /// </summary>
        [Post(RootPath + "/make-introduction")]
        Task<ApiResponse<HttpContent>> MakeIntroduction([Body] SharedSecretEncryptedPayload request, CancellationToken cancellationToken = default);
    }
}