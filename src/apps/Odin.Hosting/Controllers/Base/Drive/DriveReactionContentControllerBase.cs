using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.SQLite;
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
    protected async Task AddReaction(AddReactionRequest request, IdentityDatabase db)
    {
        await _reactionContentService.AddReactionAsync(MapToInternalFile(request.File), request.Reaction, WebOdinContext.GetCallerOdinIdOrFail(), WebOdinContext,
            db);
    }

    /// <summary />
    protected async Task DeleteReaction(DeleteReactionRequest request, IdentityDatabase db)
    {
        await _reactionContentService.DeleteReactionAsync(MapToInternalFile(request.File), request.Reaction, WebOdinContext.GetCallerOdinIdOrFail(), WebOdinContext,
            db);
    }

    /// <summary />
    protected async Task DeleteAllReactions(DeleteReactionRequest request, IdentityDatabase db)
    {
        await _reactionContentService.DeleteAllReactionsAsync(MapToInternalFile(request.File), WebOdinContext, db);
    }

    /// <summary />
    protected async Task<GetReactionsResponse> GetReactions(GetReactionsRequest request, IdentityDatabase db)
    {
        return await _reactionContentService.GetReactionsAsync(MapToInternalFile(request.File), cursor: request.Cursor,
            maxCount: request.MaxRecords, WebOdinContext, db);
    }

    /// <summary />
    protected async Task<GetReactionCountsResponse> GetReactionCounts(GetReactionsRequest request, IdentityDatabase db)
    {
        return await _reactionContentService.GetReactionCountsByFileAsync(MapToInternalFile(request.File), WebOdinContext, db);
    }

    protected async Task<List<string>> GetReactionsByIdentityAndFile(GetReactionsByIdentityRequest request, IdentityDatabase db)
    {
        return await _reactionContentService.GetReactionsByIdentityAndFileAsync(request.Identity, MapToInternalFile(request.File), WebOdinContext, db);
    }
}