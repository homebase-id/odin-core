using System.Threading.Tasks;
using Odin.Services.Peer;
using Refit;

namespace Odin.Services.EncryptionKeyService
{
    /// <summary>
    /// The interface for querying from a host to another host
    /// </summary>
    public interface IPeerEncryptionKeyServiceHttpClient
    {
        private const string Root = PeerApiPathConstants.EncryptionV1;
        
        [Get(Root + "/rsa_public_key")]
        Task<ApiResponse<GetPublicKeyResponse>> GetRsaPublicKey(PublicPrivateKeyType keyType);
        
                
        [Get(Root + "/ecc_public_key")]
        Task<ApiResponse<GetEccPublicKeyResponse>> GetEccPublicKey(PublicPrivateKeyType keyType);
    }
}