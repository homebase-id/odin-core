using System;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.Types;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.DigitalIdentityHost.Controllers.Admin
{
    [ApiController]
    [Route("/api/admin/authentication")]
    public class OwnerAuthenticationController : Controller
    {
        private readonly IOwnerAuthenticationService _authService;

        public OwnerAuthenticationController(IOwnerAuthenticationService authService)
        {
            _authService = authService;
        }
        
        [HttpPost]
        public async Task<IActionResult> Authenticate([FromBody]NonceReplyPackage package)
        {
            var result = await _authService.Authenticate(package);
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

        
        [HttpGet("nonce")]
        public async Task<IActionResult> GenerateNonce()
        {
           var result = await _authService.GenerateNonce();

           var cn = new ClientNoncePackage()
           {
               Nonce64 = result.Nonce64,
               SaltKek64 = result.SaltKek64,
               SaltPassword64 = result.SaltPassword64
           };
           return new JsonResult(cn);
        }
        
    }
}