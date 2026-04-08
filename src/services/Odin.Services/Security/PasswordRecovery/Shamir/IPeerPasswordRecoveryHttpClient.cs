using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Peer;
using Refit;

namespace Odin.Services.Security.PasswordRecovery.Shamir
{
    public interface IPeerPasswordRecoveryHttpClient
    {
        private const string PasswdRoot = PeerApiPathConstants.PasswordRecoveryV1;

        [Post(PasswdRoot + "/verify-readiness")]
        Task<ApiResponse<RemotePlayerReadinessResult>> VerifyReadiness(CancellationToken cancellationToken = default);

        [Post(PasswdRoot + "/verify-shard")]
        Task<ApiResponse<ShardVerificationResult>> VerifyShard([Body] VerifyShardRequest request, CancellationToken cancellationToken = default);

        [Post(PasswdRoot + "/request-shard")]
        Task<ApiResponse<RetrieveShardResult>> RequestShard([Body] RetrieveShardRequest request, CancellationToken cancellationToken = default);

        [Post(PasswdRoot + "/accept-player-shard")]
        Task<ApiResponse<HttpContent>> SendPlayerShard([Body] RetrieveShardResult request, CancellationToken cancellationToken = default);
    }
}