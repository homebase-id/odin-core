using System.Collections.Generic;

namespace Youverse.Core.Services.Drive;

public class QueryRecentResult
{
    /// <summary>
    /// Set to true if the metadata header was included in the results (based on the result options)
    /// </summary>
    public bool IncludeMetadataHeader { get; set; }
    
    public ulong Cursor { get; set; }
    public IEnumerable<DriveSearchResult> SearchResults { get; set; }
}