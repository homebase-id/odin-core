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

namespace Youverse.Hosting.Controllers.Transit
{
    /// <summary>
    /// Controller to enable kickoff of background tasks.  By running this over http, we keep the multi-tenant pattern working
    /// </summary>
    [ApiController]
    [Route("/api/transit/client/outbox/processor")]

    //TODO: !!! need to add a certificate for the system to make calls into itself
    //[Authorize(Policy = DotYouPolicyNames.IsSystemProcess, AuthenticationSchemes = DotYouAuthConstants.SystemCertificate)]
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
            try
            {
                //TODO: not sure I should return a detailed result here.
                //pick up the files from the outbox
                //var batch = _outbox.GetNextBatch();
                var batch = await _outbox.GetNextBatch();
                var result = await _transit.SendBatchNow(batch.Results);
                _logger.LogInformation($"Sending {batch.Results.Count} items from background controller");

                return new JsonResult(result);
            }
            catch (InvalidDataException e)
            {
                return new JsonResult(new NoResultResponse(false, e.Message));
            }
        }
    }
}