using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Hosting.Authentication.Perimeter;

namespace Odin.Hosting.Controllers.Certificate
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route("/api/perimeter/transit/encryption")]
    [Authorize(Policy = CertificatePerimeterPolicies.IsInYouverseNetwork, AuthenticationSchemes = PerimeterAuthConstants.PublicTransitAuthScheme)]
    public class EncryptionPublicKeyController : ControllerBase
    {
        private readonly IPublicKeyService _publicKeyService;
        // private Guid _stateItemId;

        public EncryptionPublicKeyController(IPublicKeyService publicKeyService)
        {
            _publicKeyService = publicKeyService;
        }

        [HttpGet("offlinekey")]
        public async Task<GetOfflinePublicKeyResponse> GetOfflinePublicKey()
        {
            var key = await _publicKeyService.GetOfflinePublicKey();

            return new GetOfflinePublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c,
                Expiration = key.expiration.milliseconds
            };
            
        }
    }
}