using System.Threading.Tasks;
using Refit;

namespace Odin.Core.Services.EncryptionKeyService
{
    /// <summary>
    /// The interface for querying from a host to another host
    /// </summary>
    public interface IEncryptionKeyServiceHttpClient
    {
        private const string Root = "/api/perimeter/transit/encryption";
        
        [Get(Root + "/offlineKey")]
        Task<ApiResponse<GetOfflinePublicKeyResponse>> GetOfflinePublicKey();

        [Get(Root + "/publickey")]
        Task<ApiResponse<GetPublicKeyResponse>> GetPublicKey(RsaKeyType keyType);
    }
}