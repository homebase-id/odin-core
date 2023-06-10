using System.Threading.Tasks;
using Odin.Hosting.Controllers.Anonymous;
using Odin.Hosting.Controllers.Anonymous.RsaKeys;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Rsa
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IRsaHttpClientForOwner
    {
        private const string RootEndpoint = YouAuthApiPathConstants.PublicKeysV1;

        [Get(RootEndpoint + "/signing")]
        Task<ApiResponse<GetPublicKeyResponse>> GetSigningPublicKey();
        
        [Get(RootEndpoint + "/online")]
        Task<ApiResponse<GetPublicKeyResponse>> GetOnlinePublicKey();
    }
}