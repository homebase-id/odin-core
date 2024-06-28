#nullable enable

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Hosting.Authentication.System;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Notifications.System
{
    [ApiController]
    [Authorize(Policy = SystemPolicies.IsSystemProcess, AuthenticationSchemes = SystemAuthConstants.SchemeName)]
    [Route(OwnerApiPathConstants.PushNotificationsV1)]
    public class OwnerSystemPushNotificationController() : OdinControllerBase
    {
        [HttpPost("process")]
        public Task<IActionResult> ProcessBatch()
        {
            throw new NotImplementedException("this is to be removed");
        }
    }
}