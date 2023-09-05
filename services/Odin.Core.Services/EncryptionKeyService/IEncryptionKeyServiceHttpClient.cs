using System.Threading.Tasks;
using Odin.Core.Services.Transit;
using Refit;

namespace Odin.Core.Services.EncryptionKeyService
{
    /// <summary>
    /// The interface for querying from a host to another host
    /// </summary>
    public interface IEncryptionKeyServiceHttpClient
    {
        private const string Root = PeerApiPathConstants.EncryptionV1;
        
        [Get(Root + "/publickey")]
        Task<ApiResponse<GetPublicKeyResponse>> GetPublicKey(RsaKeyType keyType);
    }
}