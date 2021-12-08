using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Youverse.Core;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Hosting.Security;
using Youverse.Hosting.Security.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Transit
{
    /// <summary>
    /// Controller to enable kickoff of background tasks.  By running this over http, we keep the multi-tenant pattern working
    /// </summary>
    [ApiController]
    [Route("/api/transit/client/outbox")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner, AuthenticationSchemes = OwnerAuthConstants.DotIdentityOwnerScheme)]
    public class OutboxManagementController : ControllerBase
    {
        private readonly ILogger<OutboxManagementController> _logger;
        private readonly ITransitService _transit;
        private readonly IOutboxService _outbox;

        public OutboxManagementController(ITransitService transit, IOutboxService outbox, ILogger<OutboxManagementController> logger)
        {
            _transit = transit;
            _outbox = outbox;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetList(int pageNumber, int pageSize)
        {
            var items = await _outbox.GetPendingItems(new PageOptions(pageNumber, pageSize));
            return new JsonResult(items);
        }

        [HttpGet("item")]
        public async Task<IActionResult> GetOutboxItem(Guid id)
        {
            var items = await _outbox.GetItem(id);
            return new JsonResult(items);
        }

        [HttpDelete("item")]
        public async Task<IActionResult> RemoveOutboxItem(Guid id)
        {
            await _outbox.RemoveItem(id);
            return new JsonResult(true);
        }

        [HttpPut("item/priority")]
        public async Task<IActionResult> UpdatePriority(Guid id, int priority)
        {
            await _outbox.UpdatePriority(id, priority);
            return new JsonResult(true);
        }
    }
}