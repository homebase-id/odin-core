using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Identity;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Drives.Reactions;
using Odin.Core.Services.Transit.ReceivingHost.Reactions;
using Odin.Core.Services.Transit.SendingHost;
using Odin.Hosting.Controllers.Base;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Transit
{
    /// <summary>
    /// Routes reaction requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.TransitReactionContentV1)]
    [AuthorizeValidOwnerToken]
    public class TransitReactionContentSenderController : OdinControllerBase
    {
        private readonly TransitReactionContentSenderService _transitReactionContentSenderService;

        public TransitReactionContentSenderController(TransitReactionContentSenderService transitReactionContentSenderService)
        {
            _transitReactionContentSenderService = transitReactionContentSenderService;
        }

        /// <summary>
        /// Adds a reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("add")]
        public async Task<IActionResult> AddReactionContent([FromBody] TransitAddReactionRequest request)
        {
            await _transitReactionContentSenderService.AddReaction((OdinId)request.OdinId, request.Request);
            return NoContent();
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("list")]
        public Task<GetReactionsPerimeterResponse> GetAllReactions([FromBody] TransitGetReactionsRequest request)
        {
            return _transitReactionContentSenderService.GetReactions((OdinId)request.OdinId, request.Request);
        }

        /// <summary>
        /// Deletes a specific reaction on a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("delete")]
        public async Task<IActionResult> DeleteReactionContent([FromBody] TransitDeleteReactionRequest request)
        {
            await _transitReactionContentSenderService.DeleteReaction((OdinId)request.OdinId, request.Request);
            return NoContent();
        }

        /// <summary>
        /// Deletes all reactions on given file; leave the reaction property empty
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("deleteall")]
        public async Task<IActionResult> DeleteAllReactionsOnFile([FromBody] TransitDeleteReactionRequest request)
        {
            await _transitReactionContentSenderService.DeleteAllReactions((OdinId)request.OdinId, request.Request);
            return NoContent();
        }


        /// <summary>
        /// Gets a summary of reactions for the file.  The cursor and max parameters are ignored
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("summary")]
        public async Task<GetReactionCountsResponse> GetReactionCountsByFile([FromBody] TransitGetReactionsRequest request)
        {
            return await _transitReactionContentSenderService.GetReactionCounts((OdinId)request.OdinId, request.Request);
        }

        /// <summary>
        /// Get reactions by identity and file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("listbyidentity")]
        public async Task<List<string>> GetReactionsByIdentity([FromBody] TransitGetReactionsByIdentityRequest request)
        {
            return await _transitReactionContentSenderService.GetReactionsByIdentityAndFile((OdinId)request.OdinId, request);
        }
    }
}