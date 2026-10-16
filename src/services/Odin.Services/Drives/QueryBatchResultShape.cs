using Odin.Core.Exceptions;

namespace Odin.Services.Drives;

/// <summary>
/// What each row of a query-batch result carries.  Serialized as a camelCase string.
/// </summary>
public enum QueryBatchResultShape
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
    /// Only what identifies a change: one <see cref="QueryBatchFileRef"/> per row, in <c>fileRefs</c>, and no
    /// <c>searchResults</c>.  The rows, the cursor and <c>hasMoreRows</c> are exactly those of the same query in
    /// the other shapes, so a client can compare the version tags with what it has and re-read the same page
    /// with full headers from the same cursor.  Process the rows of that re-read and continue from its cursor,
    /// not this one's: a file changed in between has moved further along the feed.
    /// </summary>
    Minimal = 2
}

public static class QueryBatchResultShapes
{
    /// <summary>
    /// The shape a request asks for: <paramref name="resultShape"/> when given, else the legacy
    /// <paramref name="includeMetadataHeader"/> flag, so a caller that sends neither keeps getting what it got
    /// before <c>resultShape</c> existed
    /// </summary>
    public static QueryBatchResultShape Resolve(QueryBatchResultShape? resultShape, bool includeMetadataHeader)
    {
        return resultShape ?? (includeMetadataHeader ? QueryBatchResultShape.Full : QueryBatchResultShape.NoContent);
    }

    /// <summary>
    /// A peer query's rows are re-encrypted by the calling server for its own caller, which it does for headers
    /// only; a minimal result would come back empty, so it is refused rather than silently lost
    /// </summary>
    public static void AssertSupportedOverPeer(QueryBatchResultShape? resultShape)
    {
        if (resultShape == QueryBatchResultShape.Minimal)
        {
            throw new OdinClientException("resultShape minimal is not supported on peer queries", OdinClientErrorCode.InvalidQuery);
        }
    }
}
