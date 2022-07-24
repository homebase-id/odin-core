using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Authentication.System;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Controllers.System
{
    /// <summary>
    /// Controller to enable kickoff of background tasks.  By running this over http, we keep the multi-tenant pattern working
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.TransitV1 + "/outbox/processor")]
    [Authorize(Policy = SystemPolicies.IsSystemProcess, AuthenticationSchemes = SystemAuthConstants.SchemeName)]
    public class OutboxProcessorController : ControllerBase
    {
        private readonly ILogger<OutboxProcessorController> _logger;
        private readonly ITransitService _transit;
        private readonly IOutboxService _outbox;

        public OutboxProcessorController(ITransitService transit, IOutboxService outbox, ILogger<OutboxProcessorController> logger)
        {
            _transit = transit;
            _outbox = outbox;
            _logger = logger;
        }

        [HttpPost("process")]
        public async Task<JsonResult> ProcessOutbox()
        {
            //TODO: not sure I should return a detailed result here.
            //pick up the files from the outbox
            //var batch = _outbox.GetNextBatch();
            var batch = await _outbox.GetNextBatch();
            _logger.LogInformation($"Sending {batch.Results.Count} items from background controller");
            await _transit.SendBatchNow(batch.Results);

            return new JsonResult(true);
        }
    }
}