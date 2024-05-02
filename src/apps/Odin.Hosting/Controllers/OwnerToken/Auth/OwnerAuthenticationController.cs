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
        private readonly TenantSystemStorage _tenantSystemStorage;

        public OwnerAuthenticationController(
            OwnerAuthenticationService authService,
            OwnerSecretService ss,
            PublicPrivateKeyService publicPrivateKeyService,
            ILogger<OwnerAuthenticationController> logger,
            TenantSystemStorage tenantSystemStorage)
        {
            _authService = authService;
            _ss = ss;
            _publicPrivateKeyService = publicPrivateKeyService;
            _logger = logger;
            _tenantSystemStorage = tenantSystemStorage;
        }

        [HttpGet("verifyToken")]
        public async Task<IActionResult> VerifyCookieBasedToken()
        {
            var value = Request.Cookies[OwnerAuthConstants.CookieName];
            if (ClientAuthenticationToken.TryParse(value ?? "", out var result))
            {
                using var cn = _tenantSystemStorage.CreateConnection();
                var isValid = await _authService.IsValidToken(result.Id, cn);
                return new JsonResult(isValid);
            }

            return new JsonResult(false);
        }

        [HttpPost]
        public async Task<OwnerAuthenticationResult> Authenticate([FromBody] PasswordReply package)
        {
            // try
            // {

            using var cn = _tenantSystemStorage.CreateConnection();
            var (result, sharedSecret) = await _authService.Authenticate(package, cn);
            AuthenticationCookieUtil.SetCookie(Response, OwnerAuthConstants.CookieName, result);
            PushNotificationCookieUtil.EnsureDeviceCookie(HttpContext);

            //TODO: need to encrypt shared secret using client public key
            return new OwnerAuthenticationResult() { SharedSecret = sharedSecret.GetKey() };

            // }
            // catch //todo: evaluate if I want to catch all exceptions here or just the authentication exception
            // {
            //     return null;
            // }
        }

        [HttpGet("logout")]
        public Task<JsonResult> ExpireCookieBasedToken()
        {
            var value = Request.Cookies[OwnerAuthConstants.CookieName];
            if (ClientAuthenticationToken.TryParse(value, out var result))
            {
                using var cn = _tenantSystemStorage.CreateConnection();
                _authService.ExpireToken(result.Id, cn);
            }

            Response.Cookies.Delete(OwnerAuthConstants.CookieName);
            return Task.FromResult(new JsonResult(true));
        }

        [HttpPost("extend")]
        public async Task<NoResultResponse> Extend(Guid token)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            await _authService.ExtendTokenLife(token, 100, cn);
            return new NoResultResponse(true);
        }

        [HttpPost("expire")]
        public NoResultResponse Expire(Guid token)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            _authService.ExpireToken(token, cn);
            return new NoResultResponse(true);
        }

        [HttpGet]
        public async Task<bool> IsValid(Guid token)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            var isValid = await _authService.IsValidToken(token, cn);
            return isValid;
        }

        [HttpGet("nonce")]
        public async Task<NonceData> GenerateAuthenticationNonce()
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            var result = await _authService.GenerateAuthenticationNonce(cn);
            return result;
        }

        [HttpPost("passwd")]
        public async Task<NoResultResponse> SetNewPassword([FromBody] PasswordReply reply)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            await _ss.SetNewPassword(reply, cn);
            return new NoResultResponse(true);
        }

        [HttpPost("resetpasswdrk")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordUsingRecoveryKeyRequest reply)
        {
            try
            {
                using var cn = _tenantSystemStorage.CreateConnection();
                await _ss.ResetPasswordUsingRecoveryKey(reply, WebOdinContext, cn);
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
            using var cn = _tenantSystemStorage.CreateConnection();
            return await _ss.IsMasterPasswordSet(cn);
        }

        [HttpGet("getsalts")]
        public async Task<NonceData> GenerateSalts()
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            var salts = await _ss.GenerateNewSalts(cn);
            return salts;
        }

        [HttpGet("publickey")]
        public async Task<GetPublicKeyResponse> GetRsaKey(RsaKeyType keyType)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            var key = await _publicPrivateKeyService.GetPublicRsaKey(keyType, cn);
            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c,
                Expiration = key.expiration.milliseconds
            };
        }
    }
}