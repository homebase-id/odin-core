using System.Collections.Generic;
using Youverse.Core.Services.Apps;

namespace Youverse.Core.Services.Drives;

public class QueryBatchResponse
{
    /// <summary>
    /// Name of this result when used in a batch-collection
    /// </summary>
    public string Name { get; set; }

    public bool IncludeMetadataHeader { get; set; }
    public string CursorState { get; set; }
    
    public IEnumerable<SharedSecretEncryptedFileHeader> SearchResults { get; set; }

    public static QueryBatchResponse FromResult(QueryBatchResult batch)
    {
        var response = new QueryBatchResponse()
        {
            IncludeMetadataHeader = batch.IncludeMetadataHeader,
            CursorState = batch.Cursor.ToState(),
            SearchResults = batch.SearchResults
        };

        return response;
    }
}