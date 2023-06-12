using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Cryptography.Data;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Hosting.Authentication.Perimeter;
using Odin.Hosting.Controllers.Anonymous.RsaKeys;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.Certificate
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route("/api/perimeter/transit/encryption")]
    [Authorize(Policy = CertificatePerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PerimeterAuthConstants.PublicTransitAuthScheme)]
    public class EncryptionPublicKeyController : ControllerBase
    {
        private readonly RsaKeyService _publicKeyService;
        // private Guid _stateItemId;

        public EncryptionPublicKeyController(RsaKeyService publicKeyService)
        {
            _publicKeyService = publicKeyService;
        }
        
        [HttpGet("publickey")]
        public async Task<GetPublicKeyResponse> GetRsaKey(RsaKeyType keyType)
        {
            RsaPublicKeyData key;
            switch (keyType)
            {
                case RsaKeyType.OfflineKey:
                    key = await _publicKeyService.GetOfflinePublicKey();
                    break;
                case RsaKeyType.OnlineKey:
                    key = await _publicKeyService.GetOnlinePublicKey();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }

            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c,
                Expiration = key.expiration.milliseconds
            };
            
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