using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Incoming;
using Youverse.Hosting.Authentication.App;

namespace Youverse.Hosting.Controllers.Apps.Transit
{
    [ApiController]
    [Route(AppApiPathConstants.TransitV1 + "/inbox")]
    [AuthorizeOwnerConsoleOrApp]
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