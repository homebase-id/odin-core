using System.Collections.Generic;

namespace Youverse.Core.Services.Drives.Reactions;

public class GetReactionsResponse
{
    public List<string> Reactions { get; set; }
        
    public int TotalCount { get; set; }
}