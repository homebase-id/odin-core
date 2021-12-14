using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Identity.DataAttribute;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Profile;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Owner
{
    [ApiController]
    [Route("/api/admin/identity")]
    [Authorize(Policy = OwnerPolicies.IsDigitalIdentityOwnerPolicyName, AuthenticationSchemes = OwnerAuthConstants.DotIdentityOwnerScheme)]
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