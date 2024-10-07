using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Peer;
using Odin.Hosting.Authentication.Peer;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.PeerIncoming
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.EncryptionV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.PublicTransitAuthScheme)]
    public class EncryptionPublicKeyController(
        PublicPrivateKeyService publicPrivateKeyService,
        TenantSystemStorage tenantSystemStorage,
        ILogger<EncryptionPublicKeyController> logger) : ControllerBase
    {
        [HttpGet("rsa_public_key")]
        public async Task<GetPublicKeyResponse> GetRsaKey(PublicPrivateKeyType keyType)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            var key = await publicPrivateKeyService.GetPublicRsaKey(keyType, cn);
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
            using var cn = tenantSystemStorage.CreateConnection();

            logger.LogDebug("Returning ecc_public_key type: {keyType}", keyType);
            var key = await publicPrivateKeyService.GetPublicEccKey(keyType, cn);
            return new GetPublicKeyResponse()
            {
                PublicKey = key.PublicKeyJwk().ToUtf8ByteArray(),
                Crc32 = key.crc32c,
                Expiration = key.expiration.milliseconds
            };
        }
    }
}