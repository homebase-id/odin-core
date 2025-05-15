using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Services.Drives.Reactions;

namespace Odin.Hosting.Controllers.Base.Drive;

/// <summary>
/// Handles reactions for files
/// </summary>
public abstract class DriveReactionContentControllerBase : OdinControllerBase
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
        await _reactionContentService.AddReactionAsync(MapToInternalFile(request.File), request.Reaction, WebOdinContext.GetCallerOdinIdOrFail(), WebOdinContext, markComplete: null);
    }

    /// <summary />
    protected async Task DeleteReaction(DeleteReactionRequest request)
    {
        await _reactionContentService.DeleteReactionAsync(MapToInternalFile(request.File), request.Reaction, WebOdinContext.GetCallerOdinIdOrFail(), WebOdinContext, markComplete: null);
    }

    /// <summary />
    protected async Task DeleteAllReactions(DeleteReactionRequest request)
    {
        await _reactionContentService.DeleteAllReactionsAsync(MapToInternalFile(request.File), WebOdinContext);
    }

    /// <summary />
    protected async Task<GetReactionsResponse> GetReactions(GetReactionsRequest request)
    {
        int.TryParse(request.Cursor, out var c);
        return await _reactionContentService.GetReactionsAsync(MapToInternalFile(request.File), cursor: c,
            maxCount: request.MaxRecords, WebOdinContext);
    }

    /// <summary />
    protected async Task<GetReactionCountsResponse> GetReactionCounts(GetReactionsRequest request)
    {
        return await _reactionContentService.GetReactionCountsByFileAsync(MapToInternalFile(request.File), WebOdinContext);
    }

    protected async Task<List<string>> GetReactionsByIdentityAndFile(GetReactionsByIdentityRequest request)
    {
        return await _reactionContentService.GetReactionsByIdentityAndFileAsync(request.Identity, MapToInternalFile(request.File), WebOdinContext);
    }
}