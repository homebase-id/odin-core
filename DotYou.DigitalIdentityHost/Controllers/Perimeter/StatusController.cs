﻿using System.Threading.Tasks;
using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.Kernel.Services.Authorization;
using DotYou.Types;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.DigitalIdentityHost.Controllers.Perimeter
{
    
    [ApiController]
    [Route("api/perimeter/status")]
    [Authorize(Policy = DotYouPolicyNames.MustBeIdentified)]
    public class StatusController : ControllerBase
    {
        private readonly IAdminClientAuthenticationService _authService;
        
        public StatusController(IAdminClientAuthenticationService authService)
        {
            _authService = authService;
        }

        [HttpPost("chat")]
        public async Task<IActionResult> GetAvailability()
        {
            //TODO: for prototrial, we'll just tell you if the 
            //individual is logged in.  longer term, we'll have
            //controls to turn chat on/off
            var loggedIn = await _authService.IsLoggedIn();
            
            return new JsonResult(loggedIn);
        }
    }
}