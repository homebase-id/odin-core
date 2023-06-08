using System.Collections.Generic;
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
    protected void AddReaction(AddReactionRequest request)
    {
        _reactionContentService.AddReaction(MapToInternalFile(request.File), request.Reaction);
    }

    /// <summary />
    protected void DeleteReaction(DeleteReactionRequest request)
    {
        _reactionContentService.DeleteReaction(MapToInternalFile(request.File), request.Reaction);
    }

    /// <summary />
    protected void DeleteAllReactions(DeleteReactionRequest request)
    {
        _reactionContentService.DeleteAllReactions(MapToInternalFile(request.File));
    }

    /// <summary />
    protected GetReactionsResponse GetReactions(GetReactionsRequest request)
    {
        return _reactionContentService.GetReactions(MapToInternalFile(request.File), cursor: request.Cursor,
            maxCount: request.MaxRecords);
    }

    /// <summary />
    protected GetReactionCountsResponse GetReactionCounts(GetReactionsRequest request)
    {
        return _reactionContentService.GetReactionCountsByFile(MapToInternalFile(request.File));
    }

    protected List<string> GetReactionsByIdentityAndFile(GetReactionsByIdentityRequest request)
    {
        return _reactionContentService.GetReactionsByIdentityAndFile(request.Identity, MapToInternalFile(request.File));
    }
}