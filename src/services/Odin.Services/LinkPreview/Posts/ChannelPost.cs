using System;
using Odin.Core.Time;

namespace Odin.Services.LinkPreview.Posts;

public class ChannelPost
{
    public Guid FileId { get; init; }
    public string ImageUrl { get; init; }
    public PostContent Content { get; init; }
    public UnixTimeUtc Modified { get; set; }
}