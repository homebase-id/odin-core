using System.Collections.Generic;
using Youverse.Core.Services.Apps;

namespace Youverse.Core.Services.Drive;

public class QueryBatchResponse
{
    
    public bool IncludeMetadataHeader { get; set; }
    public string CursorState { get; set; }
    
    public IEnumerable<ClientFileHeader> SearchResults { get; set; }

}