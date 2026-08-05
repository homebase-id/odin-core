using System;
using System.Collections.Generic;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Util;

namespace Odin.Services.Drives;

/// <summary>
/// Request body of the V2 query-batch-collection endpoint.  Separate from the V1
/// <see cref="QueryBatchCollectionRequest"/>, which gains nothing from any of this.
/// </summary>
public class QueryBatchCollectionRequestV2
{
    public List<CollectionQueryParamSectionV2> Queries { get; init; }

    /// <summary>
    /// Total record budget for the whole call, filled greedily in request order: each section is queried for
    /// whatever is left of the budget, and sections the budget never reaches come back as
    /// <see cref="QueryBatchSectionStatus.BudgetExhausted"/>.  This bounds the response size regardless of how
    /// many sections were submitted.  Required; must be at least 1.
    /// </summary>
    public int MaxRecords { get; set; }
}

public class CollectionQueryParamSectionV2
{
    /// <summary>
    /// Caller-chosen name for this section, echoed back on the matching result.  Must be unique within the
    /// request — it is the routing key.
    /// </summary>
    public string Name { get; set; }

    public Guid DriveId { get; init; }

    /// <summary>
    /// File system to query for this section.  Null falls back to the request-level value
    /// (<c>?xfst=</c> / <c>X-ODIN-FILE-SYSTEM-TYPE</c>), so a single collection can mix Standard and
    /// Comment sections.
    /// </summary>
    public FileSystemType? FileSystemType { get; set; }

    public FileQueryParams QueryParams { get; set; }

    public QueryBatchCollectionSectionOptionsV2 ResultOptionsRequest { get; set; }

    public void AssertIsValid()
    {
        OdinValidationUtils.AssertNotNullOrEmpty(this.Name, nameof(this.Name));
        OdinValidationUtils.AssertNotEmptyGuid(DriveId, "driveId");
        OdinValidationUtils.AssertNotNull(QueryParams, nameof(QueryParams));
    }
}

/// <summary>
/// Per-section result options.  This is <see cref="QueryBatchResultOptionsRequest"/> without
/// <c>MaxRecords</c>: the record budget is set once for the whole request on
/// <see cref="QueryBatchCollectionRequestV2.MaxRecords"/>, and a per-section field that the server silently
/// ignored would be worse than no field at all.
/// </summary>
public class QueryBatchCollectionSectionOptionsV2
{
    /// <summary>
    /// Base64 encoded value of the cursor state used when paging/chunking through records
    /// </summary>
    public string CursorState { get; set; }

    /// <summary>
    /// Specifies if the result set includes the metadata header (assuming the file has one)
    /// </summary>
    public bool IncludeMetadataHeader { get; set; }

    /// <summary>
    /// If true, the transfer history with-in the server metadata will be included
    /// </summary>
    public bool IncludeTransferHistory { get; set; }

    public QueryBatchSortOrder Ordering { get; set; }

    public QueryBatchSortField Sorting { get; set; }

    public QueryBatchResultOptions ToQueryBatchResultOptions(int maxRecords)
    {
        return new QueryBatchResultOptions
        {
            Cursor = string.IsNullOrEmpty(this.CursorState) ? new QueryBatchCursor() : new QueryBatchCursor(this.CursorState),
            MaxRecords = maxRecords,
            IncludeHeaderContent = this.IncludeMetadataHeader,
            IncludeTransferHistory = this.IncludeTransferHistory,
            Ordering = this.Ordering,
            Sorting = this.Sorting
        };
    }
}
