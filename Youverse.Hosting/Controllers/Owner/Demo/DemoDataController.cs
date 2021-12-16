﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Notifications;
using Youverse.Core.Services.Profile;
using Youverse.Services.Messaging.Demo;

namespace Youverse.Hosting.Controllers.Owner.Demo
{
    [ApiController]
    [Route("api/demodata")]
    //[Authorize(Policy = OwnerPolicies.IsDigitalIdentityOwnerPolicyName, AuthenticationSchemes = OwnerAuthConstants.DotIdentityOwnerScheme)]
    public class DemoDataController : ControllerBase
    {
        private IProfileService _profileService;
        private IPrototrialDemoDataService _prototrial;
        private IProfileAttributeManagementService _admin;
        private NotificationHandler _notificationHandler;
        private DotYouContext _dotYouContext;

        public DemoDataController(IProfileService profileService, IPrototrialDemoDataService prototrial, IProfileAttributeManagementService admin, NotificationHandler notificationHandler, DotYouContext dotYouContext)
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
            //await _notificationHandler.SendMessageToAllAsync($"I am a message from the DI of {_dotYouContext.HostDotYouId}.");
            
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