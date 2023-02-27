using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.Base;
using Youverse.Hosting.Controllers.OwnerToken.Drive;

namespace Youverse.Hosting.Controllers.ClientToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.DriveReactionsV1)]
    [Route(YouAuthApiPathConstants.DriveReactionsV1)]
    [AuthorizeValidExchangeGrant]
    public class DriveEmojiReactionController : DriveEmojiReactionControllerBase
    {
        /// <summary />
        public DriveEmojiReactionController(EmojiReactionService emojiReactionService) : base(emojiReactionService)
        {
        }
        
        /// <summary>
        /// Adds an emoji reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("add")]
        public IActionResult AddEmojiReaction([FromBody] AddReactionReqeust request)
        {
            base.AddReaction(request);
            return Ok();
        }

        /// <summary>
        /// Adds an emoji reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("delete")]
        public IActionResult DeleteEmojiReaction([FromBody] DeleteReactionRequest request)
        {
            base.DeleteReaction(request);
            return Ok();
        }
        
        /// <summary>
        /// Adds an emoji reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("deleteall")]
        public IActionResult DeleteAllReactionsOnFile([FromBody] DeleteReactionRequest request)
        {
            base.DeleteAllReactions(request);
            return Ok();
        }
        
        /// <summary>
        /// Adds an emoji reaction for a given file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("list")]
        public GetReactionsResponse GetAllReactions([FromBody] ExternalFileIdentifier file)
        {
            return base.GetReactions(file);
        }
        
        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("list2")]
        public GetReactionsResponse2 GetAllReactions2([FromBody] GetReactionsRequest request)
        {
            return base.GetReactions2(request);
        }
    }
    
}