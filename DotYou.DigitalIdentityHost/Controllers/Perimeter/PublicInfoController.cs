using System;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Admin.IdentityManagement;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Circle;
using DotYou.Types;
using DotYou.Types.Circle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.DigitalIdentityHost.Controllers.Perimeter
{
    /// <summary>
    /// Controller which enables the querying of any information the user deems public
    /// </summary>
    [ApiController]
    [Route("api/perimeter/publicinfo")]
    [Authorize(Policy = DotYouPolicyNames.MustBeIdentified)]
    public class PublicInfoController : ControllerBase
    {
        private readonly IAdminIdentityAttributeService _identityAttribute;

        public PublicInfoController(IAdminIdentityAttributeService identityAttribute)
        {
            _identityAttribute = identityAttribute;
        }
        
        [HttpGet("profile")]
        public async Task<IActionResult> GetPublicProfile()
        {
            //TODO: determine if we map the avatar uri to one that sends the request back through the user's DI
            var profile = await _identityAttribute.GetPublicProfile();
            return new JsonResult(profile);
        }
        
    }
}