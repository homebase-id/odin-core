using System;
using Odin.Core.Time;
using Odin.Services.Drives;

namespace Odin.Services.PublicPage.Posts;

public class ChannelPost
{
    public Guid FileId { get; init; }
    public string ImageUrl { get; init; }
    /// <summary>Guest payload URL on the post's channel drive, without the fileId and payload key</summary>
    public string PayloadUrlBase { get; init; }
    public PostContent Content { get; init; }
    public UnixTimeUtc Created { get; init; }
    public UnixTimeUtc Modified { get; set; }
    public ReactionSummary ReactionSummary { get; init; }

    /// <summary>
    /// Guest URL of payload <paramref name="key"/> on file <paramref name="fileId"/> of the channel drive,
    /// or on this post's own file when fileId is not a guid; null when there is no payload URL base
    /// </summary>
    public string PayloadUrl(string key, string fileId = null)
    {
        if (string.IsNullOrEmpty(PayloadUrlBase) || string.IsNullOrEmpty(key)) return null;
        var target = Guid.TryParse(fileId, out var id) && id != Guid.Empty ? id : FileId;
        return $"{PayloadUrlBase}&fileId={target}&key={Uri.EscapeDataString(key)}";
    }
}