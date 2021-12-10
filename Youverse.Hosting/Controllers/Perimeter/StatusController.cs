using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization;

namespace Youverse.Hosting.Controllers.Perimeter
{
    
    [ApiController]
    [Route("api/perimeter/status")]
    [Authorize(Policy = DotYouPolicyNames.MustBeIdentified)]
    public class StatusController : ControllerBase
    {
        private readonly IOwnerAuthenticationService _authService;
        
        public StatusController(IOwnerAuthenticationService authService)
        {
            _authService = authService;
        }

        [HttpPost("chat")]
        public Task<IActionResult> GetAvailability()
        {
            //TODO: Update to use the notification middleware to see if there's an active socket on one of the owner's registered devices
            throw new NotImplementedException("");            
        }
    }
}