using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.ApiExceptions.Client;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;
using System;
using System.Threading.Tasks;

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

            if (key == null)
                throw new NotFoundException("Signing ECC public key not found.");

            return new GetPublicKeyResponse()
            {
                PublicKey = key?.publicKey,
                Crc32 = key?.crc32c ?? 0
            };
        }

        [HttpGet("online_ecc")]
        public async Task<GetEccPublicKeyResponse> GetOnlineEccKey()
        {
            
            var key = await publicKeyService.GetOnlineEccPublicKeyAsync();

            if (key == null)
                throw new NotFoundException("Online ECC public key not found.");

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

            if (key == null)
                throw new NotFoundException("Offline ECC public key not found.");

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
    }
}