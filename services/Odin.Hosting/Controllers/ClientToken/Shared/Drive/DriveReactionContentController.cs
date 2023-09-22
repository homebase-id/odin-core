﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Drives.Reactions;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.DriveReactionsV1)]
    [Route(GuestApiPathConstants.DriveReactionsV1)]
    [AuthorizeValidGuestOrAppToken]
    public class DriveReactionContentController : DriveReactionContentControllerBase
    {
        /// <summary />
        public DriveReactionContentController(ReactionContentService reactionContentService) : base(reactionContentService)
        {
        }

        /// <summary>
        /// Adds a reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("add")]
        public async Task<IActionResult> AddReactionContent([FromBody] AddReactionRequest request)
        {
            await base.AddReaction(request);
            return NoContent();
        }

        /// <summary>
        /// Adds a reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("delete")]
        public async Task<IActionResult> DeleteReactionContent([FromBody] DeleteReactionRequest request)
        {
            await base.DeleteReaction(request);
            return NoContent();
        }

        /// <summary>
        /// Adds a reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("deleteall")]
        public async Task<IActionResult> DeleteAllReactionsOnFile([FromBody] DeleteReactionRequest request)
        {
            await base.DeleteAllReactions(request);
            return NoContent();
        }


        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("list")]
        public async Task<GetReactionsResponse> GetAllReactions([FromBody] GetReactionsRequest request)
        {
            return await base.GetReactions(request);
        }

        /// <summary>
        /// Gets a summary of reactions for the file.  The cursor and max parameters are ignored
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("summary")]
        public async Task<GetReactionCountsResponse> GetReactionCountsByFile([FromBody] GetReactionsRequest request)
        {
            return await base.GetReactionCounts(request);
        }
        
        /// <summary>
        /// Get reactions by identity and file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("listbyidentity")]
        public async Task<List<string>> GetReactionsByIdentity([FromBody] GetReactionsByIdentityRequest request)
        {
            return await base.GetReactionsByIdentityAndFile(request);
        }
    }
}