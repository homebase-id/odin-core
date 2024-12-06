using System;
using System.Threading.Tasks;
using Bitcoin.BIP39;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Fluff;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.EncryptionKeyService;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Auth
{
    [ApiController]
    [Route(OwnerApiPathConstants.AuthV1)]
    public class OwnerAuthenticationController : OdinControllerBase
    {
        private readonly OwnerAuthenticationService _authService;
        private readonly OwnerSecretService _ss;
        private readonly PublicPrivateKeyService _publicPrivateKeyService;
        private readonly ILogger<OwnerAuthenticationController> _logger;


        public OwnerAuthenticationController(
            OwnerAuthenticationService authService,
            OwnerSecretService ss,
            PublicPrivateKeyService publicPrivateKeyService,
            ILogger<OwnerAuthenticationController> logger)
        {
            _authService = authService;
            _ss = ss;
            _publicPrivateKeyService = publicPrivateKeyService;
            _logger = logger;
            
        }

        [HttpGet("verifyToken")]
        public async Task<IActionResult> VerifyCookieBasedToken()
        {
            var value = Request.Cookies[OwnerAuthConstants.CookieName];
            if (ClientAuthenticationToken.TryParse(value ?? "", out var result))
            {
                
                var isValid = await _authService.IsValidTokenAsync(result.Id);
                return new JsonResult(isValid);
            }

            return new JsonResult(false);
        }

        [HttpPost]
        public async Task<OwnerAuthenticationResult> Authenticate([FromBody] PasswordReply package)
        {
            var pushDeviceToken = PushNotificationCookieUtil.GetDeviceKey(HttpContext.Request);
            var (result, sharedSecret) = await _authService.AuthenticateAsync(package, pushDeviceToken.GetValueOrDefault(), WebOdinContext);
            AuthenticationCookieUtil.SetCookie(Response, OwnerAuthConstants.CookieName, result);
            PushNotificationCookieUtil.EnsureDeviceCookie(HttpContext);

            //TODO: need to encrypt shared secret using client public key
            return new OwnerAuthenticationResult() { SharedSecret = sharedSecret.GetKey() };
        }

        [HttpGet("logout")]
        public async Task<JsonResult> ExpireCookieBasedToken()
        {
            var value = Request.Cookies[OwnerAuthConstants.CookieName];
            if (ClientAuthenticationToken.TryParse(value, out var result))
            {
                await _authService.ExpireTokenAsync(result.Id);
            }

            Response.Cookies.Delete(OwnerAuthConstants.CookieName);
            return new JsonResult(true);
        }

        [HttpPost("extend")]
        public async Task<NoResultResponse> Extend(Guid token)
        {
            
            await _authService.ExtendTokenLifeAsync(token, 100);
            return new NoResultResponse(true);
        }

        [HttpPost("expire")]
        public async Task<NoResultResponse> Expire(Guid token)
        {
            await _authService.ExpireTokenAsync(token);
            return new NoResultResponse(true);
        }

        [HttpGet]
        public async Task<bool> IsValid(Guid token)
        {
            
            var isValid = await _authService.IsValidTokenAsync(token);
            return isValid;
        }

        [HttpGet("nonce")]
        public async Task<NonceData> GenerateAuthenticationNonce()
        {
            
            var result = await _authService.GenerateAuthenticationNonceAsync();
            return result;
        }

        [HttpPost("passwd")]
        public async Task<NoResultResponse> SetNewPassword([FromBody] PasswordReply reply)
        {
            
            await _ss.SetNewPasswordAsync(reply);
            return new NoResultResponse(true);
        }

        [HttpPost("resetpasswdrk")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordUsingRecoveryKeyRequest reply)
        {
            try
            {
                
                await _ss.ResetPasswordUsingRecoveryKeyAsync(reply, WebOdinContext);
            }
            catch (BIP39Exception e)
            {
                _logger.LogDebug("BIP39 failed: {message}", e.Message);
                return new UnauthorizedResult();
            }

            return new OkResult();
        }

        [HttpPost("ispasswordset")]
        public async Task<bool> IsMasterPasswordSet()
        {
            
            return await _ss.IsMasterPasswordSetAsync();
        }

        [HttpGet("getsalts")]
        public async Task<NonceData> GenerateSalts()
        {
            
            var salts = await _ss.GenerateNewSaltsAsync();
            return salts;
        }

        [HttpGet("publickey")]
        public async Task<GetPublicKeyResponse> GetRsaKey(PublicPrivateKeyType keyType)
        {
            var key = await _publicPrivateKeyService.GetPublicRsaKey(keyType);
            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c,
                Expiration = key.expiration.milliseconds
            };
        }

        [HttpGet("publickey_ecc")]
        public async Task<GetEccPublicKeyResponse> GetEccKey(PublicPrivateKeyType keyType)
        {
            var key = await _publicPrivateKeyService.GetPublicEccKeyAsync(keyType);
            var result = new GetEccPublicKeyResponse()
            {
                PublicKeyJwkBase64Url = key.PublicKeyJwkBase64Url(),
                CRC32c = key.crc32c,
                Expiration = key.expiration.milliseconds
            };

            return result;
        }

    }
}