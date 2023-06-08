using System.Threading.Tasks;
using Refit;

namespace Odin.Core.Services.EncryptionKeyService
{
    /// <summary>
    /// The interface for querying from a host to another host
    /// </summary>
    public interface IEncryptionKeyServiceHttpClient
    {
        [Get("/api/perimeter/transit/encryption/offlineKey")]
        Task<ApiResponse<GetOfflinePublicKeyResponse>> GetOfflinePublicKey();
    }
}