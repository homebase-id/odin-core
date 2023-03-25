using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveReactionContentV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveReactionContentController : DriveReactionContentControllerBase
    {
        /// <summary />
        public OwnerDriveReactionContentController(ReactionContentService reactionContentService) : base(reactionContentService)
        {
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("add")]
        public IActionResult AddReactionContent([FromBody] AddReactionRequest request)
        {
            base.AddReaction(request);
            return NoContent();
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("delete")]
        public IActionResult DeleteReactionContent([FromBody] DeleteReactionRequest request)
        {
            base.DeleteReaction(request);
            return NoContent();
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("deleteall")]
        public IActionResult DeleteAllReactionsOnFile([FromBody] DeleteReactionRequest request)
        {
            base.DeleteAllReactions(request);
            return NoContent();
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("list")]
        public GetReactionsResponse GetAllReactions2([FromBody] GetReactionsRequest request)
        {
            return base.GetReactions(request);
        }
        
        /// <summary>
        /// Gets a summary of reactions for the file.  The cursor and max parameters are ignored.
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("summary")]
        public GetReactionCountsResponse GetReactionCountsByFile([FromBody] GetReactionsRequest request)
        {
            return base.GetReactionCounts(request);
        }
        
        /// <summary>
        /// Get reactions by identity and file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("listbyidentity")]
        public List<string> GetReactionsByIdentity([FromBody] GetReactionsByIdentityRequest request)
        {
            return base.GetReactionsByIdentityAndFile(request);
        }
    }
}