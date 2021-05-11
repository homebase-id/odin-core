using System;
using System.Net;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Admin.IdentityManagement;
using DotYou.Kernel.Services.Authorization;
using DotYou.Types;
using DotYou.Types.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.TenantHost.Controllers.Security
{
    [ApiController]
    [Route("/api/admin/identity")]
    //[Authorize(Policy = DotYouPolicyNames.MustOwnThisIdentity)]
    public class AdminIdentityAttributeController : Controller
    {
        private readonly IAdminIdentityAttributeService _identService;

        public AdminIdentityAttributeController(IAdminIdentityAttributeService identService)
        {
            _identService = identService;
        }

        [HttpGet("primary")]
        public async Task<IActionResult> GetPrimaryName()
        {
            var result = await _identService.GetPrimaryName();

            if (result == null)
            {
                var x = new NameAttribute()
                {
                    Personal = "pie"
                };
                return new JsonResult(x)
                {
                    StatusCode = (int)HttpStatusCode.NoContent
                };
            }
            
            return new JsonResult(result);
        }

        [HttpGet("primary/avatar")]
        public IActionResult GetPrimaryAvatar()
        {
            //TODO: update to send the path of a stored photo
            return new JsonResult("/assets/unknown.jpg");
        }

        [HttpPost("primary")]
        public async Task<IActionResult> SavePrimaryName([FromBody] NameAttribute name)
        {
            await _identService.SavePrimaryName(name);
            
            return new JsonResult(new NoResultResponse(true));
        }
    }
}