﻿using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.EncryptionKeyService;

namespace Odin.Hosting.Controllers.PeerIncoming.Membership
{
    [ApiController]
    [Route(PeerApiPathConstants.ConnectionsV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class IntroductionsController(
        CircleNetworkIntroductionService introductionService) : OdinControllerBase
    {
        [HttpPost("make-introduction")]
        public async Task<IActionResult> ReceiveIntroduction([FromBody] SharedSecretEncryptedPayload payload)
        {
            await introductionService.ReceiveIntroductions(payload, WebOdinContext);
            return Ok();
        }
    }
}