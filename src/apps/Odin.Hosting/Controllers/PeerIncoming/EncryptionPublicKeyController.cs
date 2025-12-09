using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    [ApiExplorerSettings(GroupName = "peer-v1")]
    public class EncryptionPublicKeyController(
        PublicPrivateKeyService publicPrivateKeyService,
        ILogger<EncryptionPublicKeyController> logger) : ControllerBase
    {
        [HttpGet("ecc_public_key")]
        public async Task<IActionResult> GetEccKey(PublicPrivateKeyType keyType)
        {
            logger.LogDebug("Returning ecc_public_key type: {keyType}", keyType);
            var key = await publicPrivateKeyService.GetPublicEccKeyAsync(keyType);

            if (null == key)
            {
                logger.LogDebug("no ecc_public_key found");
                return NotFound();
            }

            logger.LogDebug("Returning ecc public key: {key}", key);

            var result = new GetEccPublicKeyResponse()
            {
                PublicKeyJwkBase64Url = key.PublicKeyJwk(),
                Expiration = key.expiration.milliseconds,
                CRC32c = key.crc32c
            };
            return new JsonResult(result);
        }
    }
}