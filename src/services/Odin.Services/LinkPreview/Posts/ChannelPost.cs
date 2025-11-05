using System;
using Odin.Core.Time;
using Odin.Services.Drives;

namespace Odin.Services.LinkPreview.Posts;

public class ChannelPost
{
    public Guid FileId { get; init; }
    public string ImageUrl { get; init; }
    public PostContent Content { get; init; }
    public UnixTimeUtc Modified { get; set; }
    public ReactionSummary ReactionSummary { get; init; }
}