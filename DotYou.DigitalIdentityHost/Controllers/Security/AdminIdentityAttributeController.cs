using System.Net;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Admin.IdentityManagement;
using DotYou.Kernel.Services.Authorization;
using DotYou.Types;
using DotYou.Types.Identity;
using Identity.DataType.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.TenantHost.Controllers.Security
{
    [ApiController]
    [Route("/api/admin/identity")]
    [Authorize(Policy = DotYouPolicyNames.MustOwnThisIdentity)]
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
                return new JsonResult(new NoResultResponse(true))
                {
                    StatusCode = (int)HttpStatusCode.NotFound
                };
            }
            
            return new JsonResult(result);
        }

        [HttpPost("primary")]
        public async Task<IActionResult> SavePrimaryName([FromBody] NameAttribute name)
        {
            await _identService.SavePrimaryName(name);
            
            return new JsonResult(new NoResultResponse(true));
        }
    }
}