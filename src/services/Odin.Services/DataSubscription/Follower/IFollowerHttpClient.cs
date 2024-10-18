using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Peer;
using Refit;

namespace Odin.Services.DataSubscription.Follower
{
    /// <summary>
    /// Sends follow/unfollow/updates to other Digital Identities
    /// </summary>
    public interface IFollowerHttpClient
    {
        private const string RootPath = PeerApiPathConstants.FollowersV1;

        [Post(RootPath + "/follow")]
        Task<ApiResponse<HttpContent>> Follow([Body] EccEncryptedPayload request);

        [Post(RootPath + "/unfollow")]
        Task<ApiResponse<HttpContent>> Unfollow();
    }
}