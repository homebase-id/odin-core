using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Drives.Reactions;
using Odin.Services.Peer.Incoming.Reactions;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.Base.Transit
{
    /// <summary>
    /// Routes reaction requests from the owner app to a target identity
    /// </summary>
    public abstract class PeerReactionContentSenderControllerBase(
        PeerReactionSenderService peerReactionSenderService) : OdinControllerBase
    {
        /// <summary>
        /// Adds a reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("add")]
        public async Task<IActionResult> AddReactionContent([FromBody] PeerAddReactionRequest request)
        {
            
            await peerReactionSenderService.AddReactionAsync((OdinId)request.OdinId, request.Request,WebOdinContext);
            return NoContent();
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("list")]
        public async Task<GetReactionsPerimeterResponse> GetAllReactions([FromBody] PeerGetReactionsRequest request)
        {
            
            return await peerReactionSenderService.GetReactionsAsync((OdinId)request.OdinId, request.Request,WebOdinContext);
        }

        /// <summary>
        /// Deletes a specific reaction on a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("delete")]
        public async Task<IActionResult> DeleteReactionContent([FromBody] PeerDeleteReactionRequest request)
        {
            await peerReactionSenderService.DeleteReactionAsync((OdinId)request.OdinId, request.Request,WebOdinContext);
            return NoContent();
        }

        /// <summary>
        /// Deletes all reactions on given file; leave the reaction property empty
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("deleteall")]
        public async Task<IActionResult> DeleteAllReactionsOnFile([FromBody] PeerDeleteReactionRequest request)
        {
            
            await peerReactionSenderService.DeleteAllReactionsAsync((OdinId)request.OdinId, request.Request,WebOdinContext);
            return NoContent();
        }


        /// <summary>
        /// Gets a summary of reactions for the file.  The cursor and max parameters are ignored
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("summary")]
        public async Task<GetReactionCountsResponse> GetReactionCountsByFile([FromBody] PeerGetReactionsRequest request)
        {
            
            return await peerReactionSenderService.GetReactionCountsAsync((OdinId)request.OdinId, request.Request,WebOdinContext);
        }

        /// <summary>
        /// Get reactions by identity and file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("listbyidentity")]
        public async Task<List<string>> GetReactionsByIdentity([FromBody] PeerGetReactionsByIdentityRequest request)
        {
            
            return await peerReactionSenderService.GetReactionsByIdentityAndFileAsync(request.OdinId, request,WebOdinContext);
        }
    }
}