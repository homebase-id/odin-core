using System.Collections.Generic;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;
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
        _emojiReactionService.AddReaction((DotYouIdentity)request.DotYouId, MapToInternalFile(request.File), request.Reaction);
    }

    /// <summary />
    protected void DeleteReaction(DeleteReactionRequest request)
    {
        _emojiReactionService.DeleteReaction((DotYouIdentity)request.DotYouId, MapToInternalFile(request.File), request.Reaction);
    }

    /// <summary />
    protected void DeleteAllReactions(DeleteReactionRequest request)
    {
        _emojiReactionService.DeleteReactions((DotYouIdentity)request.DotYouId, MapToInternalFile(request.File));
    }

    /// <summary />
    protected GetReactionsResponse GetReactions(ExternalFileIdentifier file)
    {
        return _emojiReactionService.GetReactions(MapToInternalFile(file));
    }
}