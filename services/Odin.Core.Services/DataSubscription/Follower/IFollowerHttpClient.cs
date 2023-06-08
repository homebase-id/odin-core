using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.EncryptionKeyService;
using Refit;

namespace Odin.Core.Services.DataSubscription.Follower
{
    /// <summary>
    /// Sends follow/unfollow/updates to other Digital Identities
    /// </summary>
    public interface IFollowerHttpClient
    {
        private const string RootPath = "/api/perimeter/followers";

        [Post(RootPath + "/follow")]
        Task<ApiResponse<HttpContent>> Follow([Body] RsaEncryptedPayload request);

        [Post(RootPath + "/unfollow")]
        Task<ApiResponse<HttpContent>> Unfollow();
        
    }
}