using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Owner.Data;
using DotYou.TenantHost.Security;
using DotYou.Types;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Identity.DataAttribute;

namespace DotYou.DigitalIdentityHost.Controllers.Owner
{
    [ApiController]
    [Route("/api/admin/identity")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner, AuthenticationSchemes = DotYouAuthConstants.DotIdentityOwnerScheme)]
    public class OwnerDataAttributeController : Controller
    {
        private readonly IOwnerDataAttributeManagementService _identManagementService;

        public OwnerDataAttributeController(IOwnerDataAttributeManagementService identManagementService)
        {
            _identManagementService = identManagementService;
        }

        [HttpGet("primary")]
        public async Task<NameAttribute> GetPrimaryName()
        {
            var result = await _identManagementService.GetPrimaryName();
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
            await _identManagementService.SavePrimaryName(name);
            
            return new JsonResult(new NoResultResponse(true));
        }
    }
}