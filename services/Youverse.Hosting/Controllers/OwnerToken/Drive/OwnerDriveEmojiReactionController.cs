using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.Base;
using Youverse.Hosting.Controllers.ClientToken;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveReactionsV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveEmojiReactionController : DriveEmojiReactionControllerBase
    {
        /// <summary />
        public OwnerDriveEmojiReactionController(EmojiReactionService emojiReactionService) : base(emojiReactionService)
        {
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("add")]
        public IActionResult AddEmojiReaction([FromBody] AddReactionReqeust request)
        {
            base.AddReaction(request);
            return Ok();
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("delete")]
        public IActionResult DeleteEmojiReaction([FromBody] DeleteReactionRequest request)
        {
            base.DeleteReaction(request);
            return Ok();
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("deleteall")]
        public IActionResult DeleteAllReactionsOnFile([FromBody] DeleteReactionRequest request)
        {
            base.DeleteAllReactions(request);
            return Ok();
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("list")]
        public GetReactionsResponse GetAllReactions([FromBody] ExternalFileIdentifier file)
        {
            return base.GetReactions(file);
        }
    }
}