using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
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
        var caller = SignedInCallerOrFail();
        await _reactionContentService.AddReactionAsync(await MapToInternalFileAsync(request.File), request.Reaction, caller, WebOdinContext, markComplete: null);
    }

    /// <summary />
    protected async Task DeleteReaction(DeleteReactionRequest request)
    {
        var caller = SignedInCallerOrFail();
        await _reactionContentService.DeleteReactionAsync(await MapToInternalFileAsync(request.File), request.Reaction, caller, WebOdinContext, markComplete: null);
    }

    /// <summary />
    protected async Task DeleteAllReactions(DeleteReactionRequest request)
    {
        await _reactionContentService.DeleteAllReactionsAsync(await MapToInternalFileAsync(request.File), WebOdinContext);
    }

    /// <summary />
    protected async Task<GetReactionsResponse> GetReactions(GetReactionsRequest request)
    {
        int.TryParse(request.Cursor, out var c);
        return await _reactionContentService.GetReactionsAsync(await MapToInternalFileAsync(request.File), cursor: c,
            maxCount: request.MaxRecords, WebOdinContext);
    }

    /// <summary>
    /// A reaction's author is the caller's identity. The guest route lets anonymous visitors in for its
    /// reads, so a visitor reaching a write is refused here (403) rather than failing as a server error.
    /// </summary>
    private OdinId SignedInCallerOrFail() =>
        WebOdinContext.Caller.OdinId ?? throw new OdinSecurityException("A reaction needs a signed-in caller");

    /// <summary />
    protected async Task<GetReactionCountsResponse> GetReactionCounts(GetReactionsRequest request)
    {
        return await _reactionContentService.GetReactionCountsByFileAsync(await MapToInternalFileAsync(request.File), WebOdinContext);
    }

    protected async Task<List<string>> GetReactionsByIdentityAndFile(GetReactionsByIdentityRequest request)
    {
        return await _reactionContentService.GetReactionsByIdentityAndFileAsync(request.Identity, await MapToInternalFileAsync(request.File), WebOdinContext);
    }
}