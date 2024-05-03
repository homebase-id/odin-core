using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Peer;
using Odin.Hosting.Authentication.Peer;

namespace Odin.Hosting.Controllers.PeerIncoming
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.EncryptionV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.PublicTransitAuthScheme)]
    public class EncryptionPublicKeyController(PublicPrivateKeyService publicPrivateKeyService) : ControllerBase
    {

        [HttpGet("rsa_public_key")]
        public async Task<GetPublicKeyResponse> GetRsaKey(PublicPrivateKeyType keyType)
        {
            var key = await publicPrivateKeyService.GetPublicRsaKey(keyType);
            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c,
                Expiration = key.expiration.milliseconds
            };
        }
        
        [HttpGet("ecc_public_key")]
        public async Task<GetPublicKeyResponse> GetEccKey(PublicPrivateKeyType keyType)
        {
            var key = await publicPrivateKeyService.GetPublicEccKey(keyType);
            return new GetPublicKeyResponse()
            {
                PublicKey = key.PublicKeyJwk().ToUtf8ByteArray(),
                Crc32 = key.crc32c,
                Expiration = key.expiration.milliseconds
            };
        }
    }
}