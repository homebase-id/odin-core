using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.EncryptionKeyService;

namespace Odin.Hosting.Controllers.Anonymous.RsaKeys
{
    [ApiController]
    [Route(YouAuthApiPathConstants.PublicKeysV1)]
    public class PublicKeyController : ControllerBase
    {
        private readonly PublicPrivateKeyService _publicKeyService;

        public PublicKeyController(PublicPrivateKeyService publicKeyService)
        {
            _publicKeyService = publicKeyService;
        }

        [HttpGet("signing")]
        public async Task<GetPublicKeyResponse> GetSigningKey()
        {
            var key = await _publicKeyService.GetSigningPublicKey();

            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c
            };
        }
        
        [HttpGet("online")]
        public async Task<GetPublicKeyResponse> GetOnlineKey()
        {
            var key = await _publicKeyService.GetOnlinePublicKey();

            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c
            };
        }
        
        [HttpGet("online_ecc")]
        public async Task<GetPublicKeyResponse> GetOnlineEccKey()
        {
            var key = await _publicKeyService.GetOnlineEccPublicKey();

            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c
            };
        }

        [HttpGet("offline")]
        public async Task<GetPublicKeyResponse> GetOfflinePublicKey()
        {
            var key = await _publicKeyService.GetOfflinePublicKey();
            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c
            };
        }
    }
}