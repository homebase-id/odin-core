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
using Odin.Hosting.UnifiedV2.Authentication;
using Odin.Services.Security;

namespace Odin.Hosting.Controllers.OwnerToken.Auth
{
    [ApiController]
    [Route(OwnerApiPathConstants.AuthV1)]
    public class OwnerAuthenticationController(
        OwnerAuthenticationService authService,
        OwnerSecretService ss,
        PublicPrivateKeyService publicPrivateKeyService,
        ILogger<OwnerAuthenticationController> logger)
        : OdinControllerBase
    {
        [HttpGet("verifyToken")]
        public async Task<IActionResult> VerifyCookieBasedToken()
        {
            var value = Request.Cookies[OwnerAuthConstants.CookieName];
            if (ClientAuthenticationToken.TryParse(value ?? "", out var result))
            {
                var isValid = await authService.IsValidTokenAsync(result.Id);
                if (isValid)
                {
                    await authService.ExtendTokenLife(result.Id);
                    await AddUpgradeRequiredHeaderAsync();
                }

                return new JsonResult(isValid);
            }

            return new JsonResult(false);
        }

        [HttpPost]
        public async Task<OwnerAuthenticationResult> Authenticate([FromBody] PasswordReply package)
        {
            var pushDeviceToken = PushNotificationCookieUtil.GetDeviceKey(HttpContext.Request);
            var (clientAuthToken, sharedSecret) = await authService.AuthenticateAsync(package, pushDeviceToken.GetValueOrDefault(), WebOdinContext);
            AuthenticationCookieUtil.SetCookie(Response, OwnerAuthConstants.CookieName, clientAuthToken);
            // v2 auth
            AuthenticationCookieUtil.SetCookieWithPath(Response, UnifiedAuthConstants.CookieName, clientAuthToken);
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
                await authService.ExpireTokenAsync(result.Id);
            }

            Response.Cookies.Delete(OwnerAuthConstants.CookieName);
            return new JsonResult(true);
        }

        [HttpPost("expire")]
        public async Task<NoResultResponse> Expire(Guid token)
        {
            await authService.ExpireTokenAsync(token);
            return new NoResultResponse(true);
        }

        [HttpGet]
        public async Task<bool> IsValid(Guid token)
        {
            var isValid = await authService.IsValidTokenAsync(token);
            return isValid;
        }

        [HttpGet("nonce")]
        public async Task<NonceData> GenerateAuthenticationNonce()
        {
            var result = await ss.GenerateAuthenticationNonceAsync();
            return result;
        }

        [HttpPost("passwd")]
        public async Task<NoResultResponse> SetNewPassword([FromBody] PasswordReply reply)
        {
            await ss.SetNewPasswordAsync(reply);
            return new NoResultResponse(true);
        }

        [HttpPost("resetpasswdrk")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordUsingRecoveryKeyRequest reply)
        {
            try
            {
                await ss.ResetPasswordUsingRecoveryKeyAsync(reply, WebOdinContext);
            }
            catch (BIP39Exception e)
            {
                logger.LogDebug("BIP39 failed: {message}", e.Message);
                return new UnauthorizedResult();
            }

            return new OkResult();
        }

        [HttpPost("ispasswordset")]
        public async Task<bool> IsMasterPasswordSet()
        {
            return await ss.IsMasterPasswordSetAsync();
        }

        [HttpGet("getsalts")]
        public async Task<NonceData> GenerateSalts()
        {
            var salts = await ss.GenerateNewSaltsAsync();
            return salts;
        }

        [HttpGet("publickey_ecc")]
        public async Task<GetEccPublicKeyResponse> GetEccKey(PublicPrivateKeyType keyType)
        {
            var key = await publicPrivateKeyService.GetPublicEccKeyAsync(keyType);
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