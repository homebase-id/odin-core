using System.Collections.Generic;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drives.Reactions;

namespace Youverse.Hosting.Controllers.Base;

/// <summary>
/// Handles emoji reactions for files
/// </summary>
public class DriveEmojiReactionControllerBase : OdinControllerBase
{
    private readonly EmojiReactionService _emojiReactionService;

    /// <summary />
    public DriveEmojiReactionControllerBase(EmojiReactionService emojiReactionService)
    {
        _emojiReactionService = emojiReactionService;
    }

    /// <summary />
    protected void AddReaction(AddReactionRequest request)
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
        _emojiReactionService.DeleteAllReactions(MapToInternalFile(request.File));
    }

    /// <summary />
    protected GetReactionsResponse GetReactions(GetReactionsRequest request)
    {
        return _emojiReactionService.GetReactions(MapToInternalFile(request.File), cursor: request.Cursor,
            maxCount: request.MaxRecords);
    }

    /// <summary />
    protected GetReactionCountsResponse GetReactionCounts(GetReactionsRequest request)
    {
        return _emojiReactionService.GetReactionCountsByFile(MapToInternalFile(request.File));
    }

    protected List<string> GetReactionsByIdentityAndFile(GetReactionsByIdentityRequest request)
    {
        return _emojiReactionService.GetReactionsByIdentityAndFile(request.Identity, MapToInternalFile(request.File));
    }
}