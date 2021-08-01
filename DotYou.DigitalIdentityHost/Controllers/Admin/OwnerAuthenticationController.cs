using System;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.TenantHost.Security;
using DotYou.Types;
using DotYou.Types.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.DigitalIdentityHost.Controllers.Admin
{
    [ApiController]
    [Route("/api/admin/authentication")]
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
            //note: this will intentionally ignore any error, including token parsing errors
            try
            {
                var value = Request.Cookies[DotYouAuthConstants.TokenKey];
                var result = AuthenticationResult.Parse(value);
                var isValid = await _authService.IsValidToken(result.Token);
                return new JsonResult(isValid);
            }
            catch
            {
                return new JsonResult(false);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Authenticate([FromBody] AuthenticationNonceReply package)
        {
            try
            {
                var result = await _authService.Authenticate(package);
                var value = $"{result.Token}|{result.Token2}";
                var options = new CookieOptions() {HttpOnly = true, IsEssential = true, Secure = true};
                Response.Cookies.Append(DotYouAuthConstants.TokenKey, value, options);
                return new JsonResult(true);
            }
            catch //todo: evaluate if I want to catch all exceptions here or just the authetnication exception
            {
                return new JsonResult(false);
            }
        }


        [HttpGet("logout")]
        public async Task<IActionResult> ExpireCookieBasedToken()
        {
            var value = Request.Cookies[DotYouAuthConstants.TokenKey];
            var result = AuthenticationResult.Parse(value);
            _authService.ExpireToken(result.Token);
            
            Response.Cookies.Delete(DotYouAuthConstants.TokenKey);
            
            return new JsonResult(true);
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

            var cn = new ClientNoncePackage()
            {
                Nonce64 = result.Nonce64,
                SaltKek64 = result.SaltKek64,
                SaltPassword64 = result.SaltPassword64
            };
            return new JsonResult(cn);
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
            //TODO: Need to drop this client nonce package convert nonsense
            var salts = await _ss.GenerateNewSalts();

            var cn = new ClientNoncePackage()
            {
                Nonce64 = salts.Nonce64,
                SaltKek64 = salts.SaltKek64,
                SaltPassword64 = salts.SaltPassword64
            };

            return new JsonResult(cn);
        }
    }
}