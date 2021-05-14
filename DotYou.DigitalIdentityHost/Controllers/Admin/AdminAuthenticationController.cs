using System;
using System.Security.Claims;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.TenantHost.Security;
using DotYou.TenantHost.Security.Authentication;
using DotYou.Types;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.TenantHost.Controllers.Admin
{
    [ApiController]
    [Route("/api/admin/authentication")]
    public class AdminAuthenticationController : Controller
    {
        private readonly IAdminClientAuthenticationService _authService;

        public AdminAuthenticationController(IAdminClientAuthenticationService authService)
        {
            _authService = authService;
        }
        
        [HttpPost]
        public async Task<IActionResult> Authenticate(string password)
        {
            var result = await _authService.Authenticate(password, 100);
            return new JsonResult(result);
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
        
    }
}