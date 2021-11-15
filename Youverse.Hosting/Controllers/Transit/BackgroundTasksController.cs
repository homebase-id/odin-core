using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
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
        private readonly ITransitService _transit;
        private readonly IOutboxQueueService _outbox;

        public BackgroundTasksController(ITransitService transit, IOutboxQueueService outbox)
        {
            _transit = transit;
            _outbox = outbox;
        }

        [HttpPost]
        public Task<JsonResult> SendParcel()
        {
            //Console.WriteLine($"{HttpContext.Request.Host.Host} as been stoked!");
            try
            {
                //TODO: not sure I should return a detailed result here.
                //pick up the files from the outbox
                var batch = _outbox.GetNextBatch();
                //var result = await _transit.SendBatchNow(batch);
                var result = new object();
                return Task.FromResult(new JsonResult(result));
            }
            catch (InvalidDataException e)
            {
                return Task.FromResult(new JsonResult(new NoResultResponse(false, e.Message)));
            }
        }
    }
}