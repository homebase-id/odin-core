using System;
using System.Collections.Generic;
using Youverse.Core.Services.Apps;
using Youverse.Core.Storage.Sqlite.DriveDatabase;

namespace Youverse.Core.Services.Drives;

public class QueryBatchResult
{
    public QueryBatchCursor Cursor { get; set; }

    /// <summary>
    /// Set to true if the metadata header was included in the results (based on the result options)
    /// </summary>
    public bool IncludeMetadataHeader { get; set; }
    
    public UInt64 CursorUpdatedTimestamp { get; set; }
    
    public IEnumerable<SharedSecretEncryptedFileHeader> SearchResults { get; set; }

    /// <summary>
    /// If Ordering.NewestFirst or Ordering.OldestFirst was specified, this value is populated, otherwise it is null
    /// </summary>
    public bool? HasMoreRows { get; set; }
}