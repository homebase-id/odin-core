using System.Collections.Generic;
using Odin.Core.Services.Apps;

namespace Odin.Core.Services.Drives;

public class QueryModifiedResult
{
    /// <summary>
    /// Set to true if the metadata header was included in the results (based on the result options)
    /// </summary>
    public bool IncludeHeaderContent { get; set; }
    
    public long Cursor { get; set; }

    public IEnumerable<SharedSecretEncryptedFileHeader> SearchResults { get; set; }
    
    public bool HasMoreRows { get; set; }
}