using System.Collections.Generic;

namespace Youverse.Core.Services.Drive;

public class QueryRecentResult
{
    public ulong Cursor { get; set; }
    public IEnumerable<DriveSearchResult> SearchResults { get; set; }
}