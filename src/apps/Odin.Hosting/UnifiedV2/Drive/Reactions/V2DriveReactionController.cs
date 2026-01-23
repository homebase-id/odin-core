using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Drives.Reactions;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Reactions
{
    [ApiController]
    [Route(UnifiedApiRouteConstants.ReactionsByFileId)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveReactionController(ReactionContentService reactionContentService)
        : DriveReactionContentControllerBase(reactionContentService)
    {
        [SwaggerOperation(
            Summary = "Add a reaction to a file",
            Description = "Adds a reaction (emoji or identifier) to a specific file for the current identity. " +
                          "If the same reaction already exists for the identity, the operation is idempotent.",
            Tags = [SwaggerInfo.FileReaction]
        )]
        [HttpPost("add")]
        public async Task<IActionResult> AddReactionContent(
            Guid driveId, Guid fileId,
            [FromBody] AddReactionRequest request
        )
        {
            request.File = new ExternalFileIdentifier
            {
                TargetDrive = WebOdinContext.PermissionsContext.GetTargetDrive(driveId),
                FileId = fileId
            };
            await base.AddReaction(request);
            return NoContent();
        }

        [SwaggerOperation(
            Summary = "Remove a reaction from a file",
            Description = "Removes a specific reaction from a file for the current identity. " +
                          "Only the matching reaction created by the current identity will be removed.",
            Tags = [SwaggerInfo.FileReaction]
        )]
        [HttpPost("delete")]
        public async Task<IActionResult> DeleteReactionContent(
            Guid driveId, Guid fileId,
            [FromBody] DeleteReactionRequest request
        )
        {
            request.File = new ExternalFileIdentifier
            {
                TargetDrive = WebOdinContext.PermissionsContext.GetTargetDrive(driveId),
                FileId = fileId
            };
            await base.DeleteReaction(request);
            return NoContent();
        }

        [SwaggerOperation(
            Summary = "Remove all reactions on a file for the current identity",
            Description = "Removes all reactions on the specified file that were created by the current identity. " +
                          "Reactions created by other identities are not affected.",
            Tags = [SwaggerInfo.FileReaction]
        )]
        [HttpPost("deleteall")]
        public async Task<IActionResult> DeleteAllReactionsOnFile(
            Guid driveId, Guid fileId,
            [FromBody] DeleteReactionRequest request
        )
        {
            request.File = new ExternalFileIdentifier
            {
                TargetDrive = WebOdinContext.PermissionsContext.GetTargetDrive(driveId),
                FileId = fileId
            };
            await base.DeleteAllReactions(request);
            return NoContent();
        }

        [SwaggerOperation(
            Summary = "List all reactions on a file",
            Description = "Retrieves all reactions associated with the specified file. " +
                          "Supports paging using cursor and max parameters and returns individual reaction entries " +
                          "including identity and metadata.",
            Tags = [SwaggerInfo.FileReaction]
        )]
        [HttpPost("list")]
        public async Task<GetReactionsResponse> GetAllReactions(
            Guid driveId, Guid fileId,
            [FromBody] GetReactionsRequest request
        )
        {
            request.File = new ExternalFileIdentifier
            {
                TargetDrive = WebOdinContext.PermissionsContext.GetTargetDrive(driveId),
                FileId = fileId
            };
            return await base.GetReactions(request);
        }

        [SwaggerOperation(
            Summary = "Get reaction counts for a file",
            Description = "Returns aggregated reaction counts for the specified file, grouped by reaction type. " +
                          "Paging parameters such as cursor and max are ignored for this endpoint.",
            Tags = [SwaggerInfo.FileReaction]
        )]
        [HttpPost("summary")]
        public async Task<GetReactionCountsResponse> GetReactionCountsByFile(
            Guid driveId, Guid fileId,
            [FromBody] GetReactionsRequest request
        )
        {
            request.File = new ExternalFileIdentifier
            {
                TargetDrive = WebOdinContext.PermissionsContext.GetTargetDrive(driveId),
                FileId = fileId
            };
            return await base.GetReactionCounts(request);
        }

        [SwaggerOperation(
            Summary = "List reactions on a file by identity",
            Description = "Retrieves the list of reactions placed on the specified file by a given identity. " +
                          "Returns only reaction identifiers associated with that identity.",
            Tags = [SwaggerInfo.FileReaction]
        )]
        [HttpPost("listbyidentity")]
        public async Task<List<string>> GetReactionsByIdentity(
            Guid driveId, Guid fileId,
            [FromBody] GetReactionsByIdentityRequest request
        )
        {
            request.File = new ExternalFileIdentifier
            {
                TargetDrive = WebOdinContext.PermissionsContext.GetTargetDrive(driveId),
                FileId = fileId
            };
            return await base.GetReactionsByIdentityAndFile(request);
        }
    }
}