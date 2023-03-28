﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Core.Services.Transit.ReceivingHost;
using Youverse.Core.Services.Transit.ReceivingHost.Reactions;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Hosting.Authentication.Perimeter;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.Certificate
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route("/api/perimeter/transit/host/reactions")]
    [Authorize(Policy = CertificatePerimeterPolicies.IsInYouverseNetwork, AuthenticationSchemes = PerimeterAuthConstants.TransitCertificateAuthScheme)]
    public class TransitPerimeterReactionContentController : OdinControllerBase
    {
        private readonly TransitReactionPerimeterService _reactionPerimeterService;

        public TransitPerimeterReactionContentController(TransitReactionPerimeterService reactionPerimeterService)
        {
            _reactionPerimeterService = reactionPerimeterService;
        }

        [HttpPost("add")]
        public async Task<IActionResult> AddReactionContent(SharedSecretEncryptedTransitPayload payload)
        {
            await _reactionPerimeterService.AddReaction(payload);
            return NoContent();
        }

        [HttpPost("list")]
        public async Task<GetReactionsPerimeterResponse> GetAllReactions(SharedSecretEncryptedTransitPayload payload)
        {
            return await _reactionPerimeterService.GetReactions(payload);
        }

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteReactionContent([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            await _reactionPerimeterService.DeleteReaction(payload);
            return NoContent();
        }

        [HttpPost("deleteall")]
        public async Task<IActionResult> DeleteAllReactionsOnFile([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            await _reactionPerimeterService.DeleteAllReactions(payload);
            return NoContent();
        }

        [HttpPost("summary")]
        public async Task<GetReactionCountsResponse> GetReactionCountsByFile([FromBody]SharedSecretEncryptedTransitPayload payload)
        {
            return await _reactionPerimeterService.GetReactionCountsByFile(payload);
        }

        [HttpPost("listbyidentity")]
        public async Task<List<string>> GetReactionsByIdentity([FromBody]SharedSecretEncryptedTransitPayload payload)
        {
            return await _reactionPerimeterService.GetReactionsByIdentityAndFile(payload);
        }
        
    }
}