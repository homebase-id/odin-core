using System.Collections.Generic;
using Youverse.Core.Services.Apps;

namespace Youverse.Core.Services.Drive;

public class QueryModifiedResult
{
    /// <summary>
    /// Set to true if the metadata header was included in the results (based on the result options)
    /// </summary>
    public bool IncludeMetadataHeader { get; set; }
    
    public ulong Cursor { get; set; }

    public IEnumerable<ClientFileHeader> SearchResults { get; set; }
}