namespace Odin.Services.Drives;

/// <summary>
/// Outcome of a single section of a V2 query-batch-collection.  A section-level fault never fails the
/// collection; it lands here instead, so the caller can log the degraded section identifiably rather than
/// mistaking it for a section that simply had nothing new.
/// </summary>
public enum QueryBatchSectionStatus
{
    /// <summary>The section ran and <see cref="QueryBatchCollectionSectionV2.SearchResults"/> is its result.</summary>
    Ok,

    /// <summary>
    /// The request-level record budget ran out before this section was reached, so it was not queried at all.
    /// The submitted cursor is echoed back verbatim and <c>HasMoreRows</c> is true; re-send to continue.
    /// </summary>
    BudgetExhausted,

    /// <summary>The caller has no read access to the drive.</summary>
    NoReadGrant,

    /// <summary>No drive exists with the requested id.</summary>
    DriveNotFound,

    /// <summary>The drive is archived and the caller is not the owner.</summary>
    DriveArchived,

    /// <summary>The section failed for any other reason; see <c>ErrorMessage</c> and <c>ErrorCode</c>.</summary>
    Error,
}
