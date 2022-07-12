using System;
using System.Collections.Generic;
using Youverse.Core.Services.Drive.Query.Sqlite.Storage;

namespace Youverse.Core.Services.Drive;

public class QueryBatchResult
{
    public QueryBatchCursor Cursor { get; set; }
    public UInt64 CursorUpdatedTimestamp { get; set; }
    public IEnumerable<DriveSearchResult> SearchResults { get; set; }
}