using Youverse.Core.Services.Drives.Reactions;

namespace Youverse.Hosting.Controllers.Base;

/// <summary>
/// Handles emoji reactions for files
/// </summary>
public class DriveEmojiReactionControllerBase : YouverseControllerBase
{
    private readonly EmojiReactionService _emojiReactionService;

    /// <summary />
    public DriveEmojiReactionControllerBase(EmojiReactionService emojiReactionService)
    {
        _emojiReactionService = emojiReactionService;
    }

    /// <summary />
    protected void AddReaction(AddReactionReqeust request)
    {
        _emojiReactionService.AddReaction(MapToInternalFile(request.File), request.Reaction);
    }

    /// <summary />
    protected void DeleteReaction(DeleteReactionRequest request)
    {
        _emojiReactionService.DeleteReaction(MapToInternalFile(request.File), request.Reaction);
    }

    /// <summary />
    protected void DeleteAllReactions(DeleteReactionRequest request)
    {
        _emojiReactionService.DeleteReactions(MapToInternalFile(request.File));
    }
    
    /// <summary />
    protected GetReactionsResponse GetReactions(GetReactionsRequest request)
    {
        return _emojiReactionService.GetReactions(MapToInternalFile(request.File), cursor: request.Cursor, maxCount: request.MaxRecords);
    }

    /// <summary />
    protected GetReactionCountsResponse GetReactionCounts(GetReactionsRequest request)
    {
        return _emojiReactionService.GetReactionCountsByFile(MapToInternalFile(request.File));
    }
}