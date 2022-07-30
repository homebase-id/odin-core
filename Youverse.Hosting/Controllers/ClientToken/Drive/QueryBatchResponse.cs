using System.Collections.Generic;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Controllers.ClientToken.Drive;

public class QueryBatchResponse
{
    public bool IncludeMetadataHeader { get; set; }
    public string CursorState { get; set; }
    
    public IEnumerable<ClientFileHeader> SearchResults { get; set; }

}