using System.Threading.Tasks;
using Refit;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Transit;

namespace Youverse.Core.Services.EncryptionKeyService
{
    /// <summary>
    /// The interface for querying from a host to another host
    /// </summary>
    public interface IEncryptionKeyServiceHttpClient
    {
        private const string HostRootEndpoint = "/api/perimeter/transit/encryption";

        [Get(HostRootEndpoint + "/offlineKey")]
        Task<ApiResponse<RsaPublicKeyData>> GetOfflinePublicKey();
    }
}