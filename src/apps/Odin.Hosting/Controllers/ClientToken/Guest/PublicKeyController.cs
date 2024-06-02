using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;

namespace Odin.Hosting.Controllers.ClientToken.Guest
{
    [ApiController]
    [Route(GuestApiPathConstants.PublicKeysV1)]
    public class PublicKeyController : ControllerBase
    {
        private readonly PublicPrivateKeyService _publicKeyService;
        private readonly TenantSystemStorage _tenantSystemStorage;

        public PublicKeyController(PublicPrivateKeyService publicKeyService, TenantSystemStorage tenantSystemStorage)
        {
            _publicKeyService = publicKeyService;
            _tenantSystemStorage = tenantSystemStorage;
        }

        [HttpGet("signing")]
        public async Task<GetPublicKeyResponse> GetSigningKey()
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            var key = await _publicKeyService.GetSigningPublicKey(cn);

            return new GetPublicKeyResponse()
            {
                PublicKey = key?.publicKey,
                Crc32 = key?.crc32c ?? 0
            };
        }

        [HttpGet("online")]
        public async Task<GetPublicKeyResponse> GetOnlineKey()
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            var key = await _publicKeyService.GetOnlineRsaPublicKey(cn);

            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c
            };
        }

        [HttpGet("online_ecc")]
        public async Task<GetPublicKeyResponse> GetOnlineEccKey()
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            var key = await _publicKeyService.GetOnlineEccPublicKey(cn);

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
            using var cn = _tenantSystemStorage.CreateConnection();
            var key = await _publicKeyService.GetOfflineEccPublicKey(cn);
            var expiration = Math.Min(key.expiration.seconds, 3600);
            Response.Headers.CacheControl = $"public,max-age={expiration}";
            return key.PublicKeyJwkBase64Url();
        }

        [HttpGet("notifications_pk")]
        public async Task<string> GetNotificationsPk()
        {
            // var key = await _publicKeyService.GetNotificationsPublicKey();
            // return key.GenerateEcdsaBase64Url();

            using var cn = _tenantSystemStorage.CreateConnection();
            return await _publicKeyService.GetNotificationsEccPublicKey(cn);

            // return new GetPublicKeyResponse()
            // {
            //     PublicKey = key.publicKey,
            //     Crc32 = key.crc32c
            // };
        }

        [HttpGet("offline")]
        public async Task<GetPublicKeyResponse> GetOfflinePublicKey()
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            var key = await _publicKeyService.GetOfflineRsaPublicKey(cn);
            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c
            };
        }
    }
}