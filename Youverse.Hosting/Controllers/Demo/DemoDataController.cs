using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Notifications;
using Youverse.Core.Services.Profile;
using Youverse.Hosting.Security;
using Youverse.Hosting.Security.Authentication.Owner;
using Youverse.Services.Messaging.Demo;

namespace Youverse.Hosting.Controllers.Demo
{
    [ApiController]
    [Route("api/demodata")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner, AuthenticationSchemes = OwnerAuthConstants.DotIdentityOwnerScheme)]
    public class DemoDataController : ControllerBase
    {
        private IProfileService _profileService;
        private IPrototrialDemoDataService _prototrial;
        private IOwnerDataAttributeManagementService _admin;
        private NotificationHandler _notificationHandler;
        private DotYouContext _dotYouContext;

        public DemoDataController(IProfileService profileService, IPrototrialDemoDataService prototrial, IOwnerDataAttributeManagementService admin, NotificationHandler notificationHandler, DotYouContext dotYouContext)
        {
            _profileService = profileService;
            _prototrial = prototrial;
            _admin = admin;
            _notificationHandler = notificationHandler;
            _dotYouContext = dotYouContext;
        }

        [HttpGet("profiledata")]
        public async Task<IActionResult> SetProfileData()
        {
            await _notificationHandler.SendMessageToAllAsync($"I am a message from the DI of {_dotYouContext.HostDotYouId}.");
            
            //await _prototrial.SetProfiles();
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