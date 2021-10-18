using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Contacts;
using DotYou.Kernel.Services.Demo;
using DotYou.Kernel.Services.Owner.Data;
using DotYou.TenantHost.Security;
using DotYou.Types;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace DotYou.DigitalIdentityHost.Controllers.Demo
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