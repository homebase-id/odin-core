using System.Collections.Generic;

namespace Odin.Services.Drives;

/// <summary>
/// Response of the V2 query-batch-collection endpoint.  Separate from the V1
/// <see cref="QueryBatchCollectionResponse"/>, whose wire shape must not change.
/// </summary>
public class QueryBatchCollectionResponseV2
{
    /// <summary>
    /// One entry per submitted section, in submitted order.  Sections are never reordered or dropped.
    /// </summary>
    public List<QueryBatchCollectionSectionV2> Results { get; set; } = new();
}
