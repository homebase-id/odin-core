using System.Collections.Generic;

namespace Youverse.Core.Services.Drives;

public class ReactionPreviewData
{
    public List<EmojiReactionPreview> Reactions { get; set; } = new();

    public List<CommentPreview> Comments { get; set; } = new();
}

public class EmojiReactionPreview
{
    public string Key { get; set; }
    public string Count { get; set; }
}

public class CommentPreview
{
    public string DotYouId { get; set; }
    
    public string JsonContent { get; set; }
    public List<EmojiReactionPreview> Reactions { get; set; } = new();
}