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

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DrivesV1)]
    [AuthorizeValidOwnerToken]
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
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("files/reactions/add")]
        public IActionResult AddEmojiReaction([FromBody] AddReactionReqeust request)
        {
            base.AddReaction(request);
            return Ok();
        }

        /// <summary>
        /// Adds an emoji reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("files/reaction/delete")]
        public IActionResult DeleteEmojiReaction([FromBody] DeleteReactionRequest request)
        {
            base.DeleteReaction(request);
            return Ok();
        }

        /// <summary>
        /// Adds an emoji reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("files/reaction/deleteall")]
        public IActionResult DeleteAllReactions([FromBody] DeleteReactionRequest request)
        {
            base.DeleteAllReactions(request);
            return Ok();
        }

        /// <summary>
        /// Adds an emoji reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("files/reaction/list")]
        public IActionResult GetAllReactions([FromBody] ExternalFileIdentifier file)
        {
            return new JsonResult(base.GetReactions(file));
        }
    }

}
