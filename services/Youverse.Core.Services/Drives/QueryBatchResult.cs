using System;
using System.Collections.Generic;
using Youverse.Core.Services.Apps;
using Youverse.Core.Storage.SQLite;
using Youverse.Core.Storage.SQLite.DriveDatabase;

namespace Youverse.Core.Services.Drive;

public class QueryBatchResult
{
    public QueryBatchCursor Cursor { get; set; }

    /// <summary>
    /// Set to true if the metadata header was included in the results (based on the result options)
    /// </summary>
    public bool IncludeMetadataHeader { get; set; }
    
    public UInt64 CursorUpdatedTimestamp { get; set; }
    
    public IEnumerable<SharedSecretEncryptedFileHeader> SearchResults { get; set; }

}