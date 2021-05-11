using System.Threading.Tasks;
using DotYou.Kernel.Services.Admin.IdentityManagement;
using DotYou.Types;
using DotYou.Types.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.TenantHost.Controllers.Admin
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
        public async Task<NameAttribute> GetPrimaryName()
        {
            var result = await _identService.GetPrimaryName();
            return result;
        }

        [HttpGet("primary/avatar")]
        public IActionResult GetPrimaryAvatar()
        {
            //TODO: update to send the path of a stored photo
            return new JsonResult(new AvatarUri() {Uri = "/assets/unknown.png"});
        }

        [HttpPost("primary")]
        public async Task<IActionResult> SavePrimaryName([FromBody] NameAttribute name)
        {
            await _identService.SavePrimaryName(name);
            
            return new JsonResult(new NoResultResponse(true));
        }
    }
}