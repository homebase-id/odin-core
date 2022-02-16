using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Profile;
using Youverse.Hosting.Authentication.TransitPerimeter;

namespace Youverse.Hosting.Controllers.TransitPerimeter
{
    /// <summary>
    /// Controller making available the DI owner's profile data based on the level
    /// of access the caller is given
    /// </summary>
    [ApiController]
    [Route("api/perimeter/profile")]
    [Authorize(Policy = TransitPerimeterPolicies.IsInYouverseNetwork, AuthenticationSchemes = TransitPerimeterAuthConstants.TransitAuthScheme)]
    public class ProfileController : ControllerBase
    {
        private readonly IProfileAttributeManagementService _profileAttributeService;

        public ProfileController(IProfileAttributeManagementService profileAttributeService)
        {
            _profileAttributeService = profileAttributeService;
        }


        [HttpGet]
        public async Task<IActionResult> GetProfile()
        {
            var profile = await _profileAttributeService.GetBasicPublicProfile();
            return new JsonResult(profile);
        }
    }
}