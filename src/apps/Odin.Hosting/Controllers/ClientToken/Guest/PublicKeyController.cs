using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;

namespace Odin.Hosting.Controllers.ClientToken.Guest
{
    [ApiController]
    [Route(GuestApiPathConstants.PublicKeysV1)]
    public class PublicKeyController(PublicPrivateKeyService publicKeyService, TenantSystemStorage tenantSystemStorage)
        : ControllerBase
    {
        [HttpGet("signing")]
        public async Task<GetPublicKeyResponse> GetSigningKey()
        {
            var db = tenantSystemStorage.IdentityDatabase;
            var key = await publicKeyService.GetSigningPublicKeyAsync(db);

            return new GetPublicKeyResponse()
            {
                PublicKey = key?.publicKey,
                Crc32 = key?.crc32c ?? 0
            };
        }

        [HttpGet("online")]
        public async Task<GetPublicKeyResponse> GetOnlineKey()
        {
            var db = tenantSystemStorage.IdentityDatabase;
            var key = await publicKeyService.GetOnlineRsaPublicKeyAsync(db);

            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c
            };
        }

        [HttpGet("online_ecc")]
        public async Task<GetPublicKeyResponse> GetOnlineEccKey()
        {
            var db = tenantSystemStorage.IdentityDatabase;
            var key = await publicKeyService.GetOnlineEccPublicKeyAsync(db);

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
            var db = tenantSystemStorage.IdentityDatabase;
            var key = await publicKeyService.GetOfflineEccPublicKeyAsync(db);
            var expiration = Math.Min(key.expiration.seconds, 3600);
            Response.Headers.CacheControl = $"public,max-age={expiration}";
            return key?.PublicKeyJwkBase64Url();
        }

        [HttpGet("notifications_pk")]
        public async Task<string> GetNotificationsPk()
        {
            // var key = await _publicKeyService.GetNotificationsPublicKey();
            // return key.GenerateEcdsaBase64Url();

            var db = tenantSystemStorage.IdentityDatabase;
            return await publicKeyService.GetNotificationsEccPublicKeyAsync(db);

            // return new GetPublicKeyResponse()
            // {
            //     PublicKey = key.publicKey,
            //     Crc32 = key.crc32c
            // };
        }

        [HttpGet("offline")]
        public async Task<GetPublicKeyResponse> GetOfflinePublicKey()
        {
            var db = tenantSystemStorage.IdentityDatabase;
            var key = await publicKeyService.GetOfflineRsaPublicKeyAsync(db);
            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c
            };
        }
    }
}