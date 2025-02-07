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
            
            var key = await publicKeyService.GetSigningPublicKeyAsync();

            return new GetPublicKeyResponse()
            {
                PublicKey = key?.publicKey,
                Crc32 = key?.crc32c ?? 0
            };
        }

        [HttpGet("online")]
        public async Task<GetPublicKeyResponse> GetOnlineKey()
        {
            var key = await publicKeyService.GetOnlineRsaPublicKeyPublic();

            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c
            };
        }

        [HttpGet("online_ecc")]
        public async Task<GetEccPublicKeyResponse> GetOnlineEccKey()
        {
            
            var key = await publicKeyService.GetOnlineEccPublicKeyAsync();

            return new GetEccPublicKeyResponse()
            {
                PublicKeyJwkBase64Url = key?.PublicKeyJwkBase64Url(),
                CRC32c = key?.crc32c ?? 0,
                Expiration = key?.expiration.milliseconds ?? 0
            };
        }

        [HttpGet("offline_ecc")]
        public async Task<string> GetOfflineEccPublicKey()
        {
            var key = await publicKeyService.GetOfflineEccPublicKeyAsync();
            var expiration = Math.Min(key.expiration.seconds, 3600);
            Response.Headers.CacheControl = $"public,max-age={expiration}";
            return key?.PublicKeyJwkBase64Url();
        }

        [HttpGet("notifications_pk")]
        public async Task<string> GetNotificationsPk()
        {
            // var key = await _publicKeyService.GetNotificationsPublicKey();
            // return key.GenerateEcdsaBase64Url();
            
            return await publicKeyService.GetNotificationsEccPublicKeyAsync();

            // return new GetPublicKeyResponse()
            // {
            //     PublicKey = key.publicKey,
            //     Crc32 = key.crc32c
            // };
        }

        [HttpGet("offline")]
        public async Task<GetPublicKeyResponse> GetOfflinePublicKey()
        {
            
            var key = await publicKeyService.GetOfflineRsaPublicKeyAsync();
            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c
            };
        }
    }
}