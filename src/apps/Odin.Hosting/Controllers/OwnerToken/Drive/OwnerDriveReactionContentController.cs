using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Identity;
using Odin.Services.Authentication.Owner;
using Odin.Services.Drives.Reactions;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveReactionsV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveReactionContentController : DriveReactionContentControllerBase
    {
        private readonly ReactionContentService _reactionContentService;

        /// <summary />
        public OwnerDriveReactionContentController(ReactionContentService reactionContentService) : base(reactionContentService)
        {
            _reactionContentService = reactionContentService;
        }
        
        /// <summary>
        /// Adds a reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("group-add")]
        public async Task<IActionResult> AddGroupReactionContent([FromBody] AddGroupReactionRequest request)
        {
            OdinValidationUtils.AssertValidRecipientList(request.Recipients);
            var internalFile = MapToInternalFile(request.Request.File);
            var results = await _reactionContentService.AddGroupReaction(internalFile, request.Recipients.Select(r => (OdinId)r), request.Request.Reaction);
            return new JsonResult(results);
        }

        /// <summary>
        /// Deletes a specific reaction on a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("group-delete")]
        public async Task<IActionResult> DeleteGroupReactionContent([FromBody] DeleteGroupReactionRequest request)
        {
            OdinValidationUtils.AssertValidRecipientList(request.Recipients);
            var internalFile = MapToInternalFile(request.Request.File);
            var response = await _reactionContentService.DeleteGroupReaction(internalFile, request.Recipients.Select(r => (OdinId)r), request.Request.Reaction);
            return new JsonResult(response);
        }
    }
}