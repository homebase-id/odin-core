using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.SendingHost;
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
        private readonly ITransitService _transit;

        public OutboxProcessorController(ITransitService transit)
        {
            _transit = transit;
        }


        [HttpPost("process")]
        public async Task<bool> ProcessOutbox(int batchSize)
        {
            await _transit.ProcessOutbox(batchSize);
            return true;
        }
    }
}