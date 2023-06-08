using System.Collections.Generic;
using Youverse.Core.Services.Drives.DriveCore.Query.Sqlite;

namespace Youverse.Core.Services.Drives.Reactions;

public class GetReactionCountsResponse
{
    public List<ReactionCount> Reactions { get; set; }
        
    public int Total { get; set; }
}