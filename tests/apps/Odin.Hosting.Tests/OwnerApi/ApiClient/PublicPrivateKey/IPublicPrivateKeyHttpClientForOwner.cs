using System.Threading.Tasks;
using Odin.Services.EncryptionKeyService;
using Odin.Hosting.Controllers.Anonymous;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.PublicPrivateKey
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IPublicPrivateKeyHttpClientForOwner
    {
        private const string RootEndpoint = GuestApiPathConstants.PublicKeysV1;

        [Get(RootEndpoint + "/signing")]
        Task<ApiResponse<GetPublicKeyResponse>> GetSigningPublicKey();
        
        [Get(RootEndpoint + "/online")]
        Task<ApiResponse<GetPublicKeyResponse>> GetOnlinePublicKey();
        
        [Get(RootEndpoint + "/online_ecc")]
        Task<ApiResponse<GetEccPublicKeyResponse>> GetEccOnlinePublicKey();
                
        [Get(RootEndpoint + "/offline_ecc")]
        Task<ApiResponse<string>> GetEccOfflinePublicKey();
        
        [Get(RootEndpoint + "/offline")]
        Task<ApiResponse<GetPublicKeyResponse>> GetOfflinePublicKey();

    }
}