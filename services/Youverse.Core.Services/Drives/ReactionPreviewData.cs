using System;
using System.Collections.Generic;

namespace Youverse.Core.Services.Drives;

public class ReactionPreviewData
{
    public Dictionary<Guid, EmojiReactionPreview> Reactions2 { get; set; } = new();
    public List<EmojiReactionPreview> Reactions { get; set; } = new();

    public List<CommentPreview> Comments { get; set; } = new();

    public int TotalCommentCount { get; set; }
}

public class EmojiReactionPreview
{
    public Guid Key { get; set; }

    public string ReactionContent { get; set; }
    public int Count { get; set; }
}

public class CommentPreview
{
    public string DotYouId { get; set; }

    public string JsonContent { get; set; }

    public List<EmojiReactionPreview> Reactions { get; set; } = new();
    public ulong Created { get; set; }
    public ulong Updated { get; set; }
}