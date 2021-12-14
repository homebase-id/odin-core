using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authentication;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Owner
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

        [HttpGet("verifyDeviceToken")]
        public async Task<IActionResult> VerifyDeviceToken(Guid token)
        {
            //note: this will intentionally ignore any error, including token parsing errors
            try
            {
                var isValid = await _authService.IsValidDeviceToken(token);
                return new JsonResult(isValid);
            }
            catch
            {
                return new JsonResult(false);
            }
        }


        [HttpGet("verifyToken")]
        public async Task<IActionResult> VerifyCookieBasedToken()
        {
            var value = Request.Cookies[OwnerAuthConstants.CookieName];
            if (DotYouAuthenticationResult.TryParse(value ?? "", out var result))
            {
                var isValid = await _authService.IsValidToken(result.SessionToken);
                return new JsonResult(isValid);
            }

            return new JsonResult(false);
        }

        [HttpPost]
        public async Task<IActionResult> Authenticate([FromBody] PasswordReply package)
        {
            try
            {
                var result = await _authService.Authenticate(package);
                var options = new CookieOptions()
                {
                    HttpOnly = true, 
                    IsEssential = true, 
                    Secure = true,
                    //Path = "/owner",
                    SameSite = SameSiteMode.Strict
                };

                Response.Cookies.Append(OwnerAuthConstants.CookieName, result.ToString(), options);
                return new JsonResult(true);
            }
            catch //todo: evaluate if I want to catch all exceptions here or just the authentication exception
            {
                return new JsonResult(false);
            }
        }

        [HttpPost("device")]
        public async Task<IActionResult> AuthenticateDevice([FromBody] PasswordReply package)
        {
            try
            {
                var deviceAuth = await _authService.AuthenticateDevice(package);

                //set the cookies like normal to keep the browser logged in.  this will ensure
                //the user does not have to authenticate across multiple apps
                var value = $"{deviceAuth.AuthenticationResult.SessionToken}|{deviceAuth.AuthenticationResult.ClientHalfKek}";
                var options = new CookieOptions() {HttpOnly = true, IsEssential = true, Secure = true};
                Response.Cookies.Append(OwnerAuthConstants.CookieName, value, options);

                //return only the device token to be used from the app, etc
                return new JsonResult(deviceAuth.DeviceToken);
            }
            catch //todo: evaluate if I want to catch all exceptions here or just the authentication exception
            {
                return new JsonResult(false);
            }
        }

        [HttpGet("logout")]
        public Task<JsonResult> ExpireCookieBasedToken()
        {
            var value = Request.Cookies[OwnerAuthConstants.CookieName];
            var result = DotYouAuthenticationResult.Parse(value);
            _authService.ExpireToken(result.SessionToken);

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