using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Peer;
using Refit;

namespace Odin.Services.Authentication.Owner.Shamira
{
    public interface IPeerPasswordRecoveryHttpClient
    {
        private const string PasswdRoot = PeerApiPathConstants.PasswordRecoveryV1;

        [Post(PasswdRoot + "/accept-shard")]
        Task<ApiResponse<HttpContent>> SendShard([Body] SendShardRequest request);
       
    }
}