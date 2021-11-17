using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Youverse.Core;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Security;

namespace Youverse.Hosting.Controllers.Transit
{
    /// <summary>
    /// Controller to enable kickoff of background tasks.  By running this over http, we keep the multi-tenant pattern working
    /// </summary>
    [ApiController]
    [Route("/api/transit/background/stoke")]
    //TODO: need to add a certificate for the system to make calls into itself
    //[Authorize(Policy = DotYouPolicyNames.IsSystemProcess, AuthenticationSchemes = DotYouAuthConstants.SystemCertificate)]
    public class BackgroundTasksController : ControllerBase
    {
        private readonly ILogger<BackgroundTasksController> _logger;
        private readonly ITransitService _transit;
        private readonly IOutboxService _outbox;

        public BackgroundTasksController(ITransitService transit, IOutboxService outbox, ILogger<BackgroundTasksController> logger)
        {
            _transit = transit;
            _outbox = outbox;
            _logger = logger;
        }

        [HttpPost]
        public async Task<JsonResult> SendParcel()
        {
            try
            {
                //TODO: not sure I should return a detailed result here.
                //pick up the files from the outbox
                //var batch = _outbox.GetNextBatch();
                var batch = await _outbox.GetPendingItems(PageOptions.Default);
                //var result = await _transit.SendBatchNow(batch);
                _logger.LogInformation($"Sending {batch.Results.Count} items from background controller");

                return new JsonResult(new NoResultResponse(true, ""));
            }
            catch (InvalidDataException e)
            {
                return new JsonResult(new NoResultResponse(false, e.Message));
            }
        }
    }
}