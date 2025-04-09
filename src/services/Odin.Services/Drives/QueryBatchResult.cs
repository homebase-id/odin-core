using System;
using System.Collections.Generic;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.Apps;

namespace Odin.Services.Drives;

public class QueryBatchResult
{
    public QueryBatchCursor Cursor { get; set; }

    /// <summary>
    /// Indicates when this result was generated
    /// </summary>
    public UnixTimeUtc QueryTime { get; set; }

    /// <summary>
    /// Set to true if the metadata header was included in the results (based on the result options)
    /// </summary>
    public bool IncludeMetadataHeader { get; set; }
    
   
    public IEnumerable<SharedSecretEncryptedFileHeader> SearchResults { get; set; }

    public bool HasMoreRows { get; set; }
}