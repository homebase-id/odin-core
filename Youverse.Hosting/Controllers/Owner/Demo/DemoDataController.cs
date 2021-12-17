using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Youverse.Hosting.Controllers.Owner.Demo
{
    [ApiController]
    [Route("api/demodata")]
    //[Authorize(Policy = OwnerPolicies.IsDigitalIdentityOwnerPolicyName, AuthenticationSchemes = OwnerAuthConstants.DotIdentityOwnerScheme)]
    public class DemoDataController : ControllerBase
    {
        private DemoDataGenerator _demoDataGenerator;

        public DemoDataController(DemoDataGenerator demoDataGenerator)
        {
            _demoDataGenerator = demoDataGenerator;
        }

        [HttpGet("profiledata")]
        public async Task<IActionResult> SetProfileData()
        {
            //await _notificationHandler.SendMessageToAllAsync($"I am a message from the DI of {_dotYouContext.HostDotYouId}.");
            
            await _demoDataGenerator.SetProfiles();
            return new JsonResult(true);
        }

        [HttpGet("connectionrequest")]
        public async Task<IActionResult> SendConnectionRequests()
        {
            await _demoDataGenerator.AddConnectionRequests();
            return new JsonResult(true);
        }
    }
}