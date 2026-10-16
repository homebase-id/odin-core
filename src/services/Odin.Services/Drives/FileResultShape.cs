using System;
using Odin.Core.Exceptions;

namespace Odin.Services.Drives;

/// <summary>
/// What a file is described with: each row of a query-batch result, and each drive event on a notification socket
/// (chosen at its handshake).  Serialized as a camelCase string.
/// </summary>
public enum FileResultShape
{
    /// <summary>
    /// The client file header, with <c>appData.content</c> emptied.  What <c>includeMetadataHeader: false</c> asks for.
    /// </summary>
    NoContent = 0,

    /// <summary>
    /// The full client file header.  What <c>includeMetadataHeader: true</c> asks for.
    /// </summary>
    Full = 1,

    /// <summary>
    /// Only what identifies a change: a <see cref="FileRef"/> instead of the header.  On a socket, a drive event
    /// carries <c>fileRef</c> / <c>previousFileRef</c> instead of <c>header</c> / <c>previousServerFileHeader</c>.
    /// In query-batch, the rows go in <c>fileRefs</c> and there is no <c>searchResults</c>; the rows, the cursor and <c>hasMoreRows</c> are exactly those of the same query in
    /// the other shapes, so a client can compare each row with what it has and re-read the same page with full
    /// headers from the same cursor when something is new to it.  Process the rows of that re-read and continue
    /// from its cursor, not this one's: a file changed in between has moved further along the feed.
    /// <para>
    /// What to compare: <c>updated</c> moves on every change that moves a file along the feed.  <c>versionTag</c>
    /// changes only when the header is written and <c>localVersionTag</c> only when local app data is; a reaction
    /// or a transfer-history update (delivery, read receipts) moves <c>updated</c> and neither tag.
    /// </para>
    /// </summary>
    Minimal = 2
}

public static class FileResultShapes
{
    /// <summary>
    /// The shape a request asks for: <paramref name="resultShape"/> when given, else the legacy
    /// <paramref name="includeMetadataHeader"/> flag, so a caller that sends neither keeps getting what it got
    /// before <c>resultShape</c> existed
    /// </summary>
    public static FileResultShape Resolve(FileResultShape? resultShape, bool includeMetadataHeader)
    {
        AssertKnown(resultShape);
        return resultShape ?? (includeMetadataHeader ? FileResultShape.Full : FileResultShape.NoContent);
    }

    /// <summary>
    /// The enum converter also takes numbers, and an unknown one must not quietly become some other shape
    /// </summary>
    public static void AssertKnown(FileResultShape? resultShape)
    {
        if (resultShape.HasValue && !Enum.IsDefined(resultShape.Value))
        {
            throw new OdinClientException($"Unknown resultShape {(int)resultShape.Value}", OdinClientErrorCode.InvalidQuery);
        }
    }
}
