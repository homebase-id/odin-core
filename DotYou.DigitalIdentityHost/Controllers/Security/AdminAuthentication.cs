using System;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authentication;
using DotYou.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.TenantHost.Controllers.Security
{
    [ApiController]
    [Route("/api/authentication/admin")]
    public class AdminAuthentication : Controller
    {
        private readonly IAdminClientAuthenticationService _authService;

        public AdminAuthentication(IAdminClientAuthenticationService authService)
        {
            _authService = authService;
        }

        [HttpPost]
        public async Task<IActionResult> Authenticate(string password)
        {
            Guid token = await _authService.Authenticate(password, 100);
            //WriteTokenCookie(token);
            return new JsonResult(token);
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

        private void WriteTokenCookie(Guid token)
        {
            HttpContext.Response.Cookies.Append(
                "Token",
                token.ToString(),
                new CookieOptions()
                {
                    Path = "/",
                    SameSite = SameSiteMode.Strict,
                    HttpOnly = true,
                    IsEssential = true,
                    Secure = true
                });
        }
    }
}