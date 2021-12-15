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

        [HttpGet("public")]
        public async Task<BasicProfileInfo> GetBasicPublicProfile()
        {
            var result = await _identManagementService.GetBasicPublicProfile();
            return result;
        }
        
        [HttpPost("public")]
        public async Task<IActionResult> SavePublicProfile([FromBody] BasicProfileInfo profile)
        {
            
            await _identManagementService.SavePublicProfile(profile.Name, profile.Photo);
            
            return new JsonResult(new NoResultResponse(true));
        }
        
        [HttpGet("connected")]
        public async Task<BasicProfileInfo> GetBasicConnectedProfile()
        {
            var result = await _identManagementService.GetBasicConnectedProfile();
            return result;
        }
        
        [HttpPost("connected")]
        public async Task<IActionResult> SaveConnectedProfile([FromBody] BasicProfileInfo profile)
        {
            
            await _identManagementService.SaveConnectedProfile(profile.Name, profile.Photo);
            
            return new JsonResult(new NoResultResponse(true));
        }
    }
}