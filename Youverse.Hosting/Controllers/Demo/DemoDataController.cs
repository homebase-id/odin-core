using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Profile;
using Youverse.Hosting.Security;
using Youverse.Services.Messaging.Demo;

namespace Youverse.Hosting.Controllers.Demo
{
    [ApiController]
    [Route("api/demodata")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner, AuthenticationSchemes = DotYouAuthConstants.DotIdentityOwnerScheme)]
    public class DemoDataController : ControllerBase
    {
        private IProfileService _profileService;
        private IPrototrialDemoDataService _prototrial;
        private IOwnerDataAttributeManagementService _admin;

        public DemoDataController(IProfileService profileService, IPrototrialDemoDataService prototrial, IOwnerDataAttributeManagementService admin)
        {
            _profileService = profileService;
            _prototrial = prototrial;
            _admin = admin;
        }

        [HttpGet("profiledata")]
        public async Task<IActionResult> SetProfileData()
        {
            await _prototrial.SetProfiles();
            return new JsonResult(true);
        }

        [HttpGet("connectionrequest")]
        public async Task<IActionResult> SendConnectionRequests()
        {
            await _prototrial.AddConnectionRequests();
            return new JsonResult(true);
        }
    }
}