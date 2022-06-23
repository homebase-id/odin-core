using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Transit.Incoming;

namespace Youverse.Hosting.Controllers.ClientToken.Transit
{
    [ApiController]
    [Route(AppApiPathConstants.TransitV1 + "/inbox")]
    [AuthorizeValidAppExchangeGrant]
    public class InboxController : ControllerBase
    {
        private readonly IInboxService _inboxService;

        public InboxController(IInboxService inboxService)
        {
            _inboxService = inboxService;
        }

        [HttpGet]
        public async Task<IActionResult> GetList(int pageNumber, int pageSize)
        {
            var items = await _inboxService.GetPendingItems(new PageOptions(pageNumber, pageSize));
            return new JsonResult(items);
        }

        [HttpGet("item")]
        public async Task<IActionResult> GetItem(Guid id)
        {
            var items = await _inboxService.GetItem(id);
            return new JsonResult(items);
        }

        [HttpDelete("item")]
        public async Task<IActionResult> RemoveItem(Guid id)
        {
            await _inboxService.RemoveItem(id);
            return new JsonResult(true);
        }
    }
}