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
    public class EncryptionPublicKeyController : ControllerBase
    {
        private readonly PublicPrivateKeyService _publicPrivateKeyService;
        private readonly ILogger<EncryptionPublicKeyController> _logger;

        /// <summary>
        /// Receives incoming data transfers from other hosts
        /// </summary>
        public EncryptionPublicKeyController(PublicPrivateKeyService publicPrivateKeyService,
            ILogger<EncryptionPublicKeyController> logger)
        {
            _publicPrivateKeyService = publicPrivateKeyService;
            _logger = logger;
        }

        [HttpGet("ecc_public_key")]
        public async Task<IActionResult> GetEccKey(PublicPrivateKeyType keyType)
        {
            _logger.LogDebug("Returning ecc_public_key type: {keyType}", keyType);
            var key = await _publicPrivateKeyService.GetPublicEccKeyAsync(keyType);

            if (null == key)
            {
                _logger.LogDebug("no ecc_public_key found");
                return NotFound();
            }

            _logger.LogDebug("Returning ecc public key: {key}", key);

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