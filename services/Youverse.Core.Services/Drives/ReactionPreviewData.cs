using System;
using System.Collections.Generic;

namespace Youverse.Core.Services.Drives;

public class ReactionPreviewData
{
    public Dictionary<Guid, EmojiReactionPreview> Reactions { get; set; } = new();

    public List<CommentPreview> Comments { get; set; } = new();
    
    //    public Dictionary<Guid, CommentPreview> Comments { get; set; } = new();

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
    /// <summary>
    /// The fileId of the comment.  Note: file references must be on the same drive
    /// as the file referencing them so we only need the fileId 
    /// </summary>
    public Guid FileId { get; set; }
    public string OdinId { get; set; }

    public string JsonContent { get; set; }

    public List<EmojiReactionPreview> Reactions { get; set; } = new();
    public long Created { get; set; }
    public long Updated { get; set; }
}