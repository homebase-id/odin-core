using System;
using Odin.Core.Time;
using Odin.Services.Drives;

namespace Odin.Services.PublicPage.Posts;

public class ChannelPost
{
    public Guid FileId { get; init; }
    public string ImageUrl { get; init; }
    /// <summary>Guest payload URL for this post's file, without the payload key (append &key=...)</summary>
    public string PayloadUrlBase { get; init; }
    public PostContent Content { get; init; }
    public UnixTimeUtc Modified { get; set; }
    public ReactionSummary ReactionSummary { get; init; }
}