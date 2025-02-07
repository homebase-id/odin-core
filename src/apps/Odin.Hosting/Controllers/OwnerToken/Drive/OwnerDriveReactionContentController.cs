using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Drives.Reactions;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Services.Base;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveReactionsV1)]
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
        public async Task<IActionResult> AddReactionContent([FromBody] AddReactionRequest request)
        {
            
            await base.AddReaction(request);
            return NoContent();
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("delete")]
        public async Task<IActionResult> DeleteReactionContent([FromBody] DeleteReactionRequest request)
        {
            
            await base.DeleteReaction(request);
            return NoContent();
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("deleteall")]
        public async Task<IActionResult> DeleteAllReactionsOnFile([FromBody] DeleteReactionRequest request)
        {
            
            await base.DeleteAllReactions(request);
            return NoContent();
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("list")]
        public async Task<GetReactionsResponse> GetAllReactions2([FromBody] GetReactionsRequest request)
        {
            
            return await base.GetReactions(request);
        }
        
        /// <summary>
        /// Gets a summary of reactions for the file.  The cursor and max parameters are ignored.
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("summary")]
        public async Task<GetReactionCountsResponse> GetReactionCountsByFile([FromBody] GetReactionsRequest request)
        {
            
            return await base.GetReactionCounts(request);
        }
        
        /// <summary>
        /// Get reactions by identity and file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("listbyidentity")]
        public async Task<List<string>> GetReactionsByIdentity([FromBody] GetReactionsByIdentityRequest request)
        {
            
            return await base.GetReactionsByIdentityAndFile(request);
        }
    }
}