using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Transit.SendingHost;
using Odin.Hosting.Authentication.System;
using Odin.Hosting.Controllers.OwnerToken;

namespace Odin.Hosting.Controllers.System
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
            await _transit.ProcessOutbox();
            return true;
        }
    }
}