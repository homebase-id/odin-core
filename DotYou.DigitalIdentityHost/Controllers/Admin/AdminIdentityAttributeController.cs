﻿using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Owner.IdentityManagement;
using DotYou.TenantHost.Security;
using DotYou.Types;
using DotYou.Types.DataAttribute;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.TenantHost.Controllers.Admin
{
    [ApiController]
    [Route("/api/admin/identity")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner, AuthenticationSchemes = DotYouAuthConstants.DotIdentityOwnerScheme)]
    public class AdminIdentityAttributeController : Controller
    {
        private readonly IOwnerDataAttributeService _identService;

        public AdminIdentityAttributeController(IOwnerDataAttributeService identService)
        {
            _identService = identService;
        }

        [HttpGet("primary")]
        public async Task<NameAttribute> GetPrimaryName()
        {
            var result = await _identService.GetPrimaryName();
            return result;
        }

        [HttpGet("primary/avatar")]
        public IActionResult GetPrimaryAvatar()
        {
            //TODO: update to send the path of a stored photo
            return new JsonResult(new AvatarUri() {Uri = "/assets/unknown.png"});
        }

        [HttpPost("primary")]
        public async Task<IActionResult> SavePrimaryName([FromBody] NameAttribute name)
        {
            await _identService.SavePrimaryName(name);
            
            return new JsonResult(new NoResultResponse(true));
        }
    }
}