using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Owner.Auth
{
    [ApiController]
    [Route(OwnerApiPathConstants.AuthV1)]
    public class OwnerAuthenticationController : Controller
    {
        private readonly IOwnerAuthenticationService _authService;
        private readonly IOwnerSecretService _ss;

        public OwnerAuthenticationController(IOwnerAuthenticationService authService, IOwnerSecretService ss)
        {
            _authService = authService;
            _ss = ss;
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
        public async Task<IActionResult> Authenticate([FromBody] PasswordReply package)
        {
            try
            {
                var (result, sharedSecret) = await _authService.Authenticate(package);
                var options = new CookieOptions()
                {
                    HttpOnly = true,
                    IsEssential = true,
                    Secure = true,
                    //Path = "/owner", //TODO: cannot use this until we adjust api paths
                    SameSite = SameSiteMode.Strict
                };

                Response.Cookies.Append(OwnerAuthConstants.CookieName, result.ToString(), options);

                //TODO: need to encrypt shared secret using client public key
                return new JsonResult(new OwnerAuthenticationResult() {SharedSecret = sharedSecret.GetKey()});
            }
            catch //todo: evaluate if I want to catch all exceptions here or just the authentication exception
            {
                return new JsonResult(new byte[] { });
            }
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
        public async Task<IActionResult> Extend(Guid token)
        {
            await _authService.ExtendTokenLife(token, 100);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpPost("expire")]
        public IActionResult Expire(Guid token)
        {
            _authService.ExpireToken(token);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpGet]
        public async Task<bool> IsValid(Guid token)
        {
            var isValid = await _authService.IsValidToken(token);
            return isValid;
        }

        [HttpGet("nonce")]
        public async Task<IActionResult> GenerateNonce()
        {
            var result = await _authService.GenerateAuthenticationNonce();
            return new JsonResult(result);
        }

        [HttpPost("todo_move_this")]
        public async Task<IActionResult> SetNewPassword([FromBody] PasswordReply reply)
        {
            await _ss.SetNewPassword(reply);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpGet("getsalts")]
        public async Task<IActionResult> GenerateSalts()
        {
            var salts = await _ss.GenerateNewSalts();
            return new JsonResult(salts);
        }
    }
}