using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.OwnerToken.Transit
{
    /// <summary>
    /// Routes emoji requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.TransitEmojiV1)]
    [AuthorizeValidOwnerToken]
    public class TransitEmojiController : OdinControllerBase
    {
        private readonly TransitEmojiSenderService _transitEmojiSenderService;

        public TransitEmojiController(TransitEmojiSenderService transitEmojiSenderService)
        {
            _transitEmojiSenderService = transitEmojiSenderService;
        }

        /// <summary>
        /// Adds an emoji reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("add")]
        public async Task<IActionResult> AddEmojiReaction([FromBody] TransitAddReactionRequest request)
        {
            await _transitEmojiSenderService.AddReaction((OdinId)request.OdinId, request.Request);
            return NoContent();
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("list")]
        public Task<GetReactionsResponse> GetAllReactions([FromBody] TransitGetReactionsRequest request)
        {
            return _transitEmojiSenderService.GetReactions((OdinId)request.OdinId, request.Request);
        }

        /// <summary>
        /// Adds an emoji reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("delete")]
        public IActionResult DeleteEmojiReaction([FromBody] TransitDeleteReactionRequest request)
        {
            _transitEmojiSenderService.DeleteReaction(request);
            return NoContent();
        }

        /// <summary>
        /// Adds an emoji reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("deleteall")]
        public IActionResult DeleteAllReactionsOnFile([FromBody] TransitDeleteReactionRequest request)
        {
            _transitEmojiSenderService.DeleteAllReactions(request);
            return NoContent();
        }


        /// <summary>
        /// Gets a summary of reactions for the file.  The cursor and max parameters are ignored
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("summary")]
        public async Task<GetReactionCountsResponse> GetReactionCountsByFile([FromBody] TransitGetReactionsRequest request)
        {
            return await _transitEmojiSenderService.GetReactionCounts((OdinId)request.OdinId, request.Request);
        }

        /// <summary>
        /// Get reactions by identity and file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("listbyidentity")]
        public async Task<List<string>> GetReactionsByIdentity([FromBody] TransitGetReactionsByIdentityRequest request)
        {
            return await _transitEmojiSenderService.GetReactionsByIdentityAndFile((OdinId)request.OdinId, request);
        }
    }
}