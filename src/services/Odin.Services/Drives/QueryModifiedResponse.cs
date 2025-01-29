using System.Collections.Generic;
using Odin.Services.Apps;

namespace Odin.Services.Drives;

public class QueryModifiedResponse
{
    /// <summary>
    /// Name of this result when used in a batch-collection
    /// </summary>
    public string Name { get; set; }

    public bool IncludeHeaderContent { get; set; }
    
    public string Cursor { get; set; }

    public IEnumerable<SharedSecretEncryptedFileHeader> SearchResults { get; set; }
    
    // public bool HasMoreRows { get; set; }

    public static QueryModifiedResponse FromResult(QueryModifiedResult result)
    {
        var response = new QueryModifiedResponse()
        {
            IncludeHeaderContent = result.IncludeHeaderContent,
            Cursor = result.Cursor,
            SearchResults = result.SearchResults
        };

        return response;
    }
}