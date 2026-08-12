using System.Collections.Generic;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Apps;

namespace Odin.Services.Drives;

/// <summary>
/// One section of a <see cref="QueryBatchCollectionResponseV2"/>.
///
/// Deliberately a separate type from <see cref="QueryBatchResponse"/>: that type is the return value of every
/// single-query endpoint (V1 and V2 alike) as well as the element type of the V1 collection, and neither may
/// grow the status fields below.
/// </summary>
public class QueryBatchCollectionSectionV2
{
    /// <summary>
    /// Name of this section, echoed from the request.  This is the routing key the caller matches on.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Legacy flag, kept populated so a client reading only it still works.  True for
    /// <see cref="QueryBatchSectionStatus.NoReadGrant"/>, <see cref="QueryBatchSectionStatus.DriveNotFound"/>
    /// and <see cref="QueryBatchSectionStatus.DriveArchived"/>.  Prefer <see cref="Status"/>.
    /// </summary>
    public bool InvalidDrive { get; set; }

    /// <summary>
    /// Why this section looks the way it does.  Serialized as a camelCase string.
    /// </summary>
    public QueryBatchSectionStatus Status { get; set; }

    /// <summary>
    /// Human-readable failure detail.  Null unless <see cref="Status"/> is a failure.
    /// </summary>
    public string ErrorMessage { get; set; }

    /// <summary>
    /// Client error code when one applies.  Null otherwise.
    /// </summary>
    public OdinClientErrorCode? ErrorCode { get; set; }

    /// <summary>
    /// Indicates when this result was generated
    /// </summary>
    public UnixTimeUtc QueryTime { get; set; }

    public bool IncludeMetadataHeader { get; set; }

    public string CursorState { get; set; }

    public bool HasMoreRows { get; set; }

    public IEnumerable<SharedSecretEncryptedFileHeader> SearchResults { get; set; }

    public static QueryBatchCollectionSectionV2 FromResult(string name, QueryBatchResult batch)
    {
        return new QueryBatchCollectionSectionV2
        {
            Name = name,
            Status = QueryBatchSectionStatus.Ok,
            InvalidDrive = false,
            QueryTime = batch.QueryTime,
            IncludeMetadataHeader = batch.IncludeMetadataHeader,
            CursorState = batch.Cursor.ToJson(),
            SearchResults = batch.SearchResults ?? new List<SharedSecretEncryptedFileHeader>(),
            HasMoreRows = batch.HasMoreRows
        };
    }

    /// <summary>
    /// A section that could not be run.  Consumes no budget and never fails the collection.
    /// </summary>
    public static QueryBatchCollectionSectionV2 Failed(
        string name,
        QueryBatchSectionStatus status,
        string errorMessage,
        OdinClientErrorCode? errorCode = null)
    {
        return new QueryBatchCollectionSectionV2
        {
            Name = name,
            Status = status,
            InvalidDrive = status is QueryBatchSectionStatus.NoReadGrant
                or QueryBatchSectionStatus.DriveNotFound
                or QueryBatchSectionStatus.DriveArchived,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode,
            QueryTime = UnixTimeUtc.Now(),
            SearchResults = new List<SharedSecretEncryptedFileHeader>(),
            HasMoreRows = false
        };
    }

    /// <summary>
    /// A section the record budget never reached.  The submitted cursor is echoed back <b>verbatim</b> — the
    /// caller re-sends it on the next round, so any change here silently loses or replays records.
    /// </summary>
    public static QueryBatchCollectionSectionV2 BudgetExhausted(string name, string submittedCursorState)
    {
        return new QueryBatchCollectionSectionV2
        {
            Name = name,
            Status = QueryBatchSectionStatus.BudgetExhausted,
            InvalidDrive = false,
            CursorState = submittedCursorState,
            QueryTime = UnixTimeUtc.Now(),
            SearchResults = new List<SharedSecretEncryptedFileHeader>(),
            HasMoreRows = true
        };
    }
}
