using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Drives.Reactions;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.Base.Drive;

/// <summary>
/// Handles reactions for files
/// </summary>
public class DriveReactionContentControllerBase : OdinControllerBase
{
    private readonly ReactionContentService _reactionContentService;

    /// <summary />
    public DriveReactionContentControllerBase(ReactionContentService reactionContentService)
    {
        _reactionContentService = reactionContentService;
    }

    /// <summary />
    /// <summary>
    /// Adds a reaction for a given file
    /// </summary>
    /// <param name="request"></param>
    [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
    [HttpPost("add")]
    protected async Task<IActionResult> AddReaction(AddReactionRequest request)
    {
        await _reactionContentService.AddReaction(MapToInternalFile(request.File), request.Reaction);
        return NoContent();
    }

    [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
    [HttpPost("delete")]
    protected async Task<IActionResult> DeleteReaction(DeleteReactionRequest request)
    {
        await _reactionContentService.DeleteReaction(MapToInternalFile(request.File), request.Reaction);
        return NoContent();
    }

    [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
    [HttpPost("deleteall")]
    protected async Task DeleteAllReactions(DeleteReactionRequest request)
    {
        await _reactionContentService.DeleteAllReactions(MapToInternalFile(request.File));
    }

    [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
    [HttpPost("list")]
    protected async Task<GetReactionsResponse> GetReactions(GetReactionsRequest request)
    {
        return await _reactionContentService.GetReactions(MapToInternalFile(request.File), cursor: request.Cursor,
            maxCount: request.MaxRecords);
    }

    /// <summary>
    /// Gets a summary of reactions for the file.  The cursor and max parameters are ignored
    /// </summary>
    [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
    [HttpPost("summary")]
    protected async Task<GetReactionCountsResponse> GetReactionCounts(GetReactionsRequest request)
    {
        return await _reactionContentService.GetReactionCountsByFile(MapToInternalFile(request.File));
    }

    /// <summary>
    /// Get reactions by identity and file
    /// </summary>
    [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
    [HttpPost("listbyidentity")]
    protected async Task<List<string>> GetReactionsByIdentityAndFile(GetReactionsByIdentityRequest request)
    {
        return await _reactionContentService.GetReactionsByIdentityAndFile(request.Identity, MapToInternalFile(request.File));
    }
}