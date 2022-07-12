using System.Collections.Generic;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Controllers.ClientToken.Drive;

public class QueryBatchResponse
{
    public string CursorState { get; set; }
    public IEnumerable<DriveSearchResult> SearchResults { get; set; }
}