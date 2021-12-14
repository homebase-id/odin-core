using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Inbox;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Owner.Transit
{
    [ApiController]
    [Route("/api/transit/client/inbox")]
    [Authorize(Policy = OwnerPolicies.IsDigitalIdentityOwnerPolicyName, AuthenticationSchemes = OwnerAuthConstants.DotIdentityOwnerScheme)]
    public class InboxController : ControllerBase
    {
        private readonly IInboxService _inbox;

        public InboxController(ITransitService svc, IInboxService inbox)
        {
            _inbox = inbox;
        }

        [HttpGet]
        public async Task<IActionResult> GetList(int pageNumber, int pageSize)
        {
            var items = await _inbox.GetPendingItems(new PageOptions(pageNumber, pageSize));
            return new JsonResult(items);
        }

        [HttpGet("item")]
        public async Task<IActionResult> GetItem(Guid id)
        {
            var items = await _inbox.GetItem(id);
            return new JsonResult(items);
        }

        [HttpDelete("item")]
        public async Task<IActionResult> RemoveItem(Guid id)
        {
            await _inbox.RemoveItem(id);
            return new JsonResult(true);
        }
    }
}