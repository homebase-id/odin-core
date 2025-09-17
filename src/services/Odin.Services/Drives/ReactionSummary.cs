using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Core.Util;
using System;
using System.Collections.Generic;
using Serilog;
using Serilog.Core;

namespace Odin.Services.Drives;

public class ReactionSummary
{
    public static readonly int MaxReactionsCount = 5;
    public static readonly int MaxCommentsCount = 5;

    public Dictionary<Guid, ReactionContentPreview> Reactions { get; set; } = new();

    public List<CommentPreview> Comments { get; set; } = new();

    public int TotalCommentCount { get; set; }

    public bool TryValidate()
    {
        try
        {
            Validate();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Validate()
    {
        if (Reactions != null)
        {
            if (Reactions.Count > MaxReactionsCount)
                throw new OdinClientException($"Too many Reactions count {Reactions.Count} in ReactionSummary max {MaxReactionsCount}");

            foreach (var reaction in Reactions)
            {
                reaction.Value.Validate();
            }
        }

        if (Comments != null)
        {
            if (Comments.Count > MaxCommentsCount)
                throw new OdinClientException($"Too many Comments count {Comments.Count} in ReactionSummary max {MaxCommentsCount}");

            foreach (var comment in Comments)
            {
                comment?.Validate();
            }
        }
    }
}

public class ReactionContentPreview
{
    public static readonly int MaxReactionContentLength = 22;

    public Guid Key { get; set; }

    public string ReactionContent { get; set; }
    public int Count { get; set; }


    public bool TryValidate()
    {
        try
        {
            Validate();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Validate()
    {
        if (ReactionContent?.Length > MaxReactionContentLength)
            throw new OdinClientException(
                $"Too large ReactionContentPreview length {ReactionContent.Length} in ReactionSummary max {MaxReactionContentLength}");
    }
}

public class CommentPreview
{
    public static readonly int MaxReactionsCount = 5;
    public static readonly int MaxContentLength = 500;

    /// <summary>
    /// The fileId of the comment.  Note: file references must be on the same drive
    /// as the file referencing them so we only need the fileId
    /// </summary>
    public Guid FileId { get; set; }

    public string OdinId { get; set; }

    public string Content { get; set; }

    public List<ReactionContentPreview> Reactions { get; set; } = new();
    public UnixTimeUtc Created { get; set; }
    public UnixTimeUtc Updated { get; set; }
    public bool IsEncrypted { get; set; }

    public bool TryValidate()
    {
        try
        {
            Validate();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Validate()
    {
        if (OdinId != null)
            AsciiDomainNameValidator.AssertValidDomain(OdinId); // Because it's not an OdinId we need to check

        if (Content.Length > MaxContentLength)
        {
            var ex = new OdinClientException($"Too long Content length {Content.Length} in CommentPreview max {MaxContentLength}",
                OdinClientErrorCode.MaxContentLengthExceeded);

            // sorry seb
            Log.Debug(ex, "Content length too much for comment, len: {a}", Content.Length);
            throw ex;
        }

        if (Reactions != null)
        {
            if (Reactions.Count > MaxReactionsCount)
                throw new OdinClientException($"Too many Reactions count {Reactions.Count} in CommentPreview max {MaxReactionsCount}");

            foreach (var reaction in Reactions)
            {
                reaction.Validate();
            }
        }
    }
}