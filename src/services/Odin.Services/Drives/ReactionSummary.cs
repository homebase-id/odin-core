using System;
using System.Collections.Generic;
using Odin.Core.Identity;

namespace Odin.Services.Drives;

public class ReactionSummary
{
    public Dictionary<Guid, ReactionContentPreview> Reactions { get; set; } = new();

    public List<CommentPreview> Comments { get; set; } = new();

    public int TotalCommentCount { get; set; }
}

public class ReactionContentPreview
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
    public OdinId OdinId { get; set; }

    public string Content { get; set; }

    public List<ReactionContentPreview> Reactions { get; set; } = new();
    public long Created { get; set; }
    public long Updated { get; set; }
    public bool IsEncrypted { get; set; }
}
