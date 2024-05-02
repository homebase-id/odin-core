using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.SQLite;
using Odin.Services.Drives.Reactions;

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
    protected async Task AddReaction(AddReactionRequest request, DatabaseConnection cn)
    {
        await _reactionContentService.AddReaction(MapToInternalFile(request.File), request.Reaction, WebOdinContext, cn);
    }

    /// <summary />
    protected async Task DeleteReaction(DeleteReactionRequest request, DatabaseConnection cn)
    {
        await _reactionContentService.DeleteReaction(MapToInternalFile(request.File), request.Reaction, WebOdinContext, cn);
    }

    /// <summary />
    protected async Task DeleteAllReactions(DeleteReactionRequest request, DatabaseConnection cn)
    {
        await _reactionContentService.DeleteAllReactions(MapToInternalFile(request.File), WebOdinContext, cn);
    }

    /// <summary />
    protected async Task<GetReactionsResponse> GetReactions(GetReactionsRequest request, DatabaseConnection cn)
    {
        return await _reactionContentService.GetReactions(MapToInternalFile(request.File), cursor: request.Cursor,
            maxCount: request.MaxRecords, WebOdinContext, cn);
    }

    /// <summary />
    protected async Task<GetReactionCountsResponse> GetReactionCounts(GetReactionsRequest request, DatabaseConnection cn)
    {
        return await _reactionContentService.GetReactionCountsByFile(MapToInternalFile(request.File), WebOdinContext, cn);
    }

    protected async Task<List<string>> GetReactionsByIdentityAndFile(GetReactionsByIdentityRequest request, DatabaseConnection cn)
    {
        return await _reactionContentService.GetReactionsByIdentityAndFile(request.Identity, MapToInternalFile(request.File), WebOdinContext, cn);
    }
}
