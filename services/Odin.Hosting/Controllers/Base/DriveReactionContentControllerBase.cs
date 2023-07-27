using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Services.Drives.Reactions;

namespace Odin.Hosting.Controllers.Base;

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
    protected async Task AddReaction(AddReactionRequest request)
    {
        await _reactionContentService.AddReaction(MapToInternalFile(request.File), request.Reaction);
    }

    /// <summary />
    protected async Task DeleteReaction(DeleteReactionRequest request)
    {
        await _reactionContentService.DeleteReaction(MapToInternalFile(request.File), request.Reaction);
    }

    /// <summary />
    protected async Task DeleteAllReactions(DeleteReactionRequest request)
    {
        await _reactionContentService.DeleteAllReactions(MapToInternalFile(request.File));
    }

    /// <summary />
    protected async Task<GetReactionsResponse> GetReactions(GetReactionsRequest request)
    {
        return await _reactionContentService.GetReactions(MapToInternalFile(request.File), cursor: request.Cursor,
            maxCount: request.MaxRecords);
    }

    /// <summary />
    protected async Task<GetReactionCountsResponse> GetReactionCounts(GetReactionsRequest request)
    {
        return await _reactionContentService.GetReactionCountsByFile(MapToInternalFile(request.File));
    }

    protected async Task<List<string>> GetReactionsByIdentityAndFile(GetReactionsByIdentityRequest request)
    {
        return await _reactionContentService.GetReactionsByIdentityAndFile(request.Identity, MapToInternalFile(request.File));
    }
}