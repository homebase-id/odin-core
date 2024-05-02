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
    public class PeerReactionContentSenderControllerBase(
        PeerReactionSenderService peerReactionSenderService,
        TenantSystemStorage tenantSystemStorage) : OdinControllerBase
    {
        /// <summary>
        /// Adds a reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("add")]
        public async Task<IActionResult> AddReactionContent([FromBody] PeerAddReactionRequest request)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await peerReactionSenderService.AddReaction((OdinId)request.OdinId, request.Request,WebOdinContext, cn);
            return NoContent();
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("list")]
        public Task<GetReactionsPerimeterResponse> GetAllReactions([FromBody] PeerGetReactionsRequest request)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            return peerReactionSenderService.GetReactions((OdinId)request.OdinId, request.Request,WebOdinContext, cn);
        }

        /// <summary>
        /// Deletes a specific reaction on a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("delete")]
        public async Task<IActionResult> DeleteReactionContent([FromBody] PeerDeleteReactionRequest request)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await peerReactionSenderService.DeleteReaction((OdinId)request.OdinId, request.Request,WebOdinContext, cn);
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
            using var cn = tenantSystemStorage.CreateConnection();
            await peerReactionSenderService.DeleteAllReactions((OdinId)request.OdinId, request.Request,WebOdinContext, cn);
            return NoContent();
        }


        /// <summary>
        /// Gets a summary of reactions for the file.  The cursor and max parameters are ignored
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("summary")]
        public async Task<GetReactionCountsResponse> GetReactionCountsByFile([FromBody] PeerGetReactionsRequest request)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            return await peerReactionSenderService.GetReactionCounts((OdinId)request.OdinId, request.Request,WebOdinContext, cn);
        }

        /// <summary>
        /// Get reactions by identity and file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("listbyidentity")]
        public async Task<List<string>> GetReactionsByIdentity([FromBody] PeerGetReactionsByIdentityRequest request)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            return await peerReactionSenderService.GetReactionsByIdentityAndFile(request.OdinId, request,WebOdinContext, cn);
        }
    }
}