using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;

namespace Odin.Hosting.Controllers.ClientToken.Guest
{
    [ApiController]
    [Route(GuestApiPathConstants.PublicKeysV1)]
    public class PublicKeyController(PublicPrivateKeyService publicKeyService) : ControllerBase
    {
        [HttpGet("signing")]
        public async Task<GetPublicKeyResponse> GetSigningKey()
        {
            
            var key = await publicKeyService.GetSigningPublicKey();

            return new GetPublicKeyResponse()
            {
                PublicKey = key?.publicKey,
                Crc32 = key?.crc32c ?? 0
            };
        }

        [HttpGet("online")]
        public async Task<GetPublicKeyResponse> GetOnlineKey()
        {
            var key = await publicKeyService.GetOnlineRsaPublicKey();

            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c
            };
        }

        [HttpGet("online_ecc")]
        public async Task<GetPublicKeyResponse> GetOnlineEccKey()
        {
            
            var key = await publicKeyService.GetOnlineEccPublicKey();

            return new GetPublicKeyResponse()
            {
                PublicKey = key?.publicKey,
                Crc32 = key?.crc32c ?? 0,
                Expiration = key?.expiration.milliseconds ?? 0
            };
        }

        [HttpGet("offline_ecc")]
        public async Task<string> GetOfflineEccPublicKey()
        {
            var key = await publicKeyService.GetOfflineEccPublicKey();
            var expiration = Math.Min(key.expiration.seconds, 3600);
            Response.Headers.CacheControl = $"public,max-age={expiration}";
            return key?.PublicKeyJwkBase64Url();
        }

        [HttpGet("notifications_pk")]
        public async Task<string> GetNotificationsPk()
        {
            // var key = await _publicKeyService.GetNotificationsPublicKey();
            // return key.GenerateEcdsaBase64Url();
            
            return await publicKeyService.GetNotificationsEccPublicKey();

            // return new GetPublicKeyResponse()
            // {
            //     PublicKey = key.publicKey,
            //     Crc32 = key.crc32c
            // };
        }

        [HttpGet("offline")]
        public async Task<GetPublicKeyResponse> GetOfflinePublicKey()
        {
            
            var key = await publicKeyService.GetOfflineRsaPublicKey();
            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c
            };
        }
    }
}