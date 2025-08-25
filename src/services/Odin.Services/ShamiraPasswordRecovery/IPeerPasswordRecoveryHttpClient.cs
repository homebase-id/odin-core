using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Peer;
using Refit;

namespace Odin.Services.ShamiraPasswordRecovery
{
    public interface IPeerPasswordRecoveryHttpClient
    {
        private const string PasswdRoot = PeerApiPathConstants.PasswordRecoveryV1;

        [Post(PasswdRoot + "/verify-shard")]
        Task<ApiResponse<ShardVerificationResult>> VerifyShard([Body] VerifyShardRequest request);
       
    }
}