using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Data;
using Odin.Core.Fluff;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Hosting.Authentication.ClientToken;
using Odin.Hosting.Authentication.Owner;

namespace Odin.Hosting.Controllers.OwnerToken.Auth
{
    [ApiController]
    [Route(OwnerApiPathConstants.AuthV1)]
    public class OwnerAuthenticationController : Controller
    {
        private readonly OwnerAuthenticationService _authService;
        private readonly OwnerSecretService _ss;
        private readonly PublicPrivateKeyService _publicPrivateKeyService;

        public OwnerAuthenticationController(OwnerAuthenticationService authService, OwnerSecretService ss, PublicPrivateKeyService publicPrivateKeyService)
        {
            _authService = authService;
            _ss = ss;
            _publicPrivateKeyService = publicPrivateKeyService;
        }

        [HttpGet("verifyToken")]
        public async Task<IActionResult> VerifyCookieBasedToken()
        {
            var value = Request.Cookies[OwnerAuthConstants.CookieName];
            if (ClientAuthenticationToken.TryParse(value ?? "", out var result))
            {
                var isValid = await _authService.IsValidToken(result.Id);
                return new JsonResult(isValid);
            }

            return new JsonResult(false);
        }

        [HttpPost]
        public async Task<OwnerAuthenticationResult> Authenticate([FromBody] PasswordReply package)
        {
            // try
            // {
            
                var (result, sharedSecret) = await _authService.Authenticate(package);
                AuthenticationCookieUtil.SetCookie(Response, OwnerAuthConstants.CookieName, result);

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
            var result = ClientAuthenticationToken.Parse(value);
            _authService.ExpireToken(result.Id);

            Response.Cookies.Delete(OwnerAuthConstants.CookieName);

            return Task.FromResult(new JsonResult(true));
        }

        [HttpPost("extend")]
        public async Task<NoResultResponse> Extend(Guid token)
        {
            await _authService.ExtendTokenLife(token, 100);
            return new NoResultResponse(true);
        }

        [HttpPost("expire")]
        public NoResultResponse Expire(Guid token)
        {
            _authService.ExpireToken(token);
            return new NoResultResponse(true);
        }

        [HttpGet]
        public async Task<bool> IsValid(Guid token)
        {
            var isValid = await _authService.IsValidToken(token);
            return isValid;
        }

        [HttpGet("nonce")]
        public async Task<NonceData> GenerateAuthenticationNonce()
        {
            var result = await _authService.GenerateAuthenticationNonce();
            return result;
        }

        [HttpPost("passwd")]
        public async Task<NoResultResponse> SetNewPassword([FromBody] PasswordReply reply)
        {
            await _ss.SetNewPassword(reply);
            return new NoResultResponse(true);
        }
        
        [HttpPost("resetpasswdrk")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordUsingRecoveryKeyRequest reply)
        {
            await _ss.ResetPasswordUsingRecoveryKey(reply);
            return new OkResult();
        }
        
        [HttpPost("ispasswordset")]
        public async Task<bool> IsMasterPasswordSet()
        {
            return await _ss.IsMasterPasswordSet();
        }

        [HttpGet("getsalts")]
        public async Task<NonceData> GenerateSalts()
        {
            var salts = await _ss.GenerateNewSalts();
            return salts;
        }
        
        [HttpGet("publickey")]
        public async Task<GetPublicKeyResponse> GetRsaKey(RsaKeyType keyType)
        {
            var key = await _publicPrivateKeyService.GetPublicRsaKey(keyType);
            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c,
                Expiration = key.expiration.milliseconds
            };
        }
    }
}